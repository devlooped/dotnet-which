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

namespace Devlooped;

[Description("Discovers .NET versions used in the current directory recursively and emits a versions file.")]
public partial class InitCommand : AsyncCommand<EnvSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, EnvSettings settings)
    {
        var path = settings.VersionsFile;
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? "");

        var xml = new ConcurrentBag<string>();
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Debugger.IsAttached ? 1 : Environment.ProcessorCount,
        };

        await AnsiConsole.Status().StartAsync("Discovering .NET SDK projects...", async ctx =>
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
                    ctx.Status($"Discovering .NET SDK projects ({xml.Count})...");
                }
                catch (XmlException xe)
                {
                    // Ignore files that are not valid XML or do not match the expected structure
                    AnsiConsole.MarkupLineInterpolated($"[grey]{xe.Message} ({candidate})[/]");
                }
                catch (Exception ex)
                {
                    // Ignore files that are not valid XML or do not match the expected structure
                    AnsiConsole.MarkupLineInterpolated($"[grey]{ex.Message} ({candidate})[/]");
                }
            });
        });

        AnsiConsole.MarkupLine($":check_box_with_check: Discovered .NET SDK projects: [green]{xml.Count}[/]");

        var versions = new ConcurrentDictionary<string, byte>();
        if (DotNetMuxer.MuxerPath is not { } dotnet)
        {
            AnsiConsole.MarkupLine("[red]No .NET SDK found.[/]");
            return 1;
        }

        await AnsiConsole.Status().StartAsync("Discovering .NET versions...", async ctx =>
        {
            await Parallel.ForEachAsync(xml, options, async (candidate, cancellation) =>
            {
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
                    AnsiConsole.MarkupLineInterpolated($"[grey]{ex.Message} ({candidate})[/]");
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
                    ctx.Status($"Discovering .NET versions ({versions.Count})...");
                }
            });
        });

        if (versions.Count > 0)
        {
            AnsiConsole.MarkupLine($":check_box_with_check: Discovered .NET versions: [green]{string.Join(", ", versions.Keys.OrderBy(x => x))}[/]");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(versions.Keys.OrderBy(x => x), new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }));
            AnsiConsole.MarkupLineInterpolated($":backhand_index_pointing_right: [blue][link={Path.GetFullPath(path)}]{path}[/][/]");
        }
        else
        {
            AnsiConsole.MarkupLine(":warning: No .NET major versions found.");
        }

        return 0;
    }

    [GeneratedRegex(@"^net(coreapp)?(\d+)\.\d+$")]
    private static partial Regex NetExpr();
}