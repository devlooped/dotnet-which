using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using CliWrap;
using CliWrap.Buffered;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Json;
using WhichSettings = Devlooped.WhichCommand.WhichSettings;

namespace Devlooped;

[Description("Discovers .NET versions used in the current directory recursively")]
public partial class WhichCommand : AsyncCommand<WhichSettings>
{
    public class WhichSettings : CommandSettings
    {
        [Description("Whether to write versions to an output file. File path defaults to " + Constants.DefaultPath)]
        [CommandOption("-o|--output [file]")]
        public FlagValue<string?> Output { get; set; } = new FlagValue<string?>();
    }

    public override async Task<int> ExecuteAsync(CommandContext context, WhichSettings settings)
    {
        var path = !settings.Output.IsSet ? null :
            settings.Output.Value ?? Constants.DefaultPath;

        if (Path.GetDirectoryName(path) is { Length: > 0 } dir)
            Directory.CreateDirectory(dir);

        // We must be quiet if no output path is specified, since we'll need pure JSON 
        // output to be able to pipe the output to other commands, such as `jq` or `grep`.
        var quiet = string.IsNullOrEmpty(path);
        var xml = new ConcurrentBag<string>();
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Debugger.IsAttached ? 1 : Environment.ProcessorCount,
        };

        if (quiet)
        {
            await DiscoverSdkFilesAsync(xml, options, new Progress<string>(s => { }), new Progress<string>(s => { }));
        }
        else
        {
            await AnsiConsole.Status().StartAsync("Discovering .NET SDK projects...", async ctx =>
            {
                await DiscoverSdkFilesAsync(xml, options,
                    new Progress<string>(x => ctx.Status(x)),
                    new Progress<string>(x => AnsiConsole.MarkupLineInterpolated($"[grey]{x}[/]")));
            });
        }

        if (!quiet)
            AnsiConsole.MarkupLine($":check_box_with_check: Discovered .NET SDK projects: [green]{xml.Count}[/]");

        var versions = new ConcurrentDictionary<string, byte>();
        if (DotNetMuxer.MuxerPath is not { } dotnet)
        {
            AnsiConsole.MarkupLine("[red]No .NET SDK found.[/]");
            return 1;
        }

        if (quiet)
        {
            await CollectVersionsAsync(xml, versions, dotnet, options, new Progress<string>(s => { }), new Progress<string>(s => { }));
        }
        else
        {
            await AnsiConsole.Status().StartAsync("Discovering .NET versions...", async ctx =>
            {
                await CollectVersionsAsync(xml, versions, dotnet, options,
                    new Progress<string>(x => ctx.Status(x)),
                    new Progress<string>(x => AnsiConsole.MarkupLineInterpolated($"[grey]{x}[/]")));
            });
        }

        var json = JsonSerializer.Serialize(versions.Keys.OrderBy(x => x), new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        if (string.IsNullOrWhiteSpace(path))
        {
            AnsiConsole.Write(new JsonText(json));
        }
        else
        {
            await File.WriteAllTextAsync(path, json);
            AnsiConsole.MarkupLineInterpolated($":backhand_index_pointing_right: [blue][link={Path.GetFullPath(path)}]{path}[/][/]");
        }

        return 0;
    }

    static async Task CollectVersionsAsync(
        ConcurrentBag<string> xml, ConcurrentDictionary<string, byte> versions,
        string dotnet, ParallelOptions options,
        IProgress<string> progress, IProgress<string> logger)
        => await Parallel.ForEachAsync(xml, options, async (candidate, cancellation) =>
    {
        progress.Report($"Discovering .NET versions ({versions.Count})... {Path.GetFileName(candidate)}");
        string? output = null;
        try
        {
            var result = await Cli.Wrap(dotnet)
                .WithArguments(["msbuild", candidate, "-getProperty:TargetFramework"])
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync(cancellationToken: cancellation);

            if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                output = result.StandardOutput.Trim();
            }
            else
            {
                result = await Cli.Wrap(dotnet)
                    .WithArguments(["msbuild", candidate, "-getProperty:TargetFrameworks"])
                    .WithValidation(CommandResultValidation.None)
                    .ExecuteBufferedAsync(cancellationToken: cancellation);

                if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StandardOutput))
                {
                    output = result.StandardOutput.Trim();
                }
            }
        }
        catch (Exception ex)
        {
            logger.Report($"[grey]{ex.Message} ({candidate})[/]");
        }

        if (!string.IsNullOrWhiteSpace(output))
        {
            var frameworks = output.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var framework in frameworks)
            {
                var m = NetExpr().Match(framework);
                if (m.Success)
                    versions.TryAdd($"{m.Groups[2].Value}.x", 0);
            }
            progress.Report($"Discovering .NET versions ({versions.Count})...");
        }
    });

    static async Task DiscoverSdkFilesAsync(ConcurrentBag<string> xml, ParallelOptions options, IProgress<string> progress, IProgress<string> logger)
    {
        var candidates = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.*proj", SearchOption.AllDirectories);
        await Parallel.ForEachAsync(candidates, options, async (candidate, _) =>
        {
            try
            {
                using var reader = XmlReader.Create(candidate, new XmlReaderSettings
                {
                    IgnoreComments = true,
                    IgnoreWhitespace = true,
                    Async = true,
                });

                if (await reader.MoveToContentAsync() != XmlNodeType.Element || reader.Name != "Project" ||
                    !reader.MoveToAttribute("Sdk"))
                    return; // Not a valid project file

                xml.Add(candidate);
                progress.Report($"Discovering .NET SDK projects ({xml.Count})...");
            }
            catch (XmlException xe)
            {
                // Ignore files that are not valid XML or do not match the expected structure
                logger.Report($"[grey]{xe.Message} ({candidate})[/]");
            }
            catch (Exception ex)
            {
                // Ignore files that are not valid XML or do not match the expected structure
                logger.Report($"[grey]{ex.Message} ({candidate})[/]");
                //AnsiConsole.MarkupLineInterpolated($"[grey]{ex.Message} ({candidate})[/]");
            }
        });
    }

    [GeneratedRegex(@"^net(coreapp)?(\d+)\.\d+$")]
    private static partial Regex NetExpr();
}