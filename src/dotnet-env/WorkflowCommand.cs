using System.ComponentModel;
using System.IO;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Devlooped;

[Description("Creates a GitHub Actions workflow file for dotnet-env")]
public class WorkflowCommand : Command<EnvSettings>
{
    public override int Execute(CommandContext context, EnvSettings settings)
    {
        var path = "./.github/workflows/dotnet-env.yml";
        if (File.Exists(path))
        {
            AnsiConsole.Markup($"[yellow]Workflow file already exists at {path}[/]");
            return 1;
        }

        Directory.CreateDirectory(path);
        var content = ThisAssembly.Resources.dotnet_env.Text;
        if (settings.VersionsFile != EnvSettings.DefaultPath)
            content = content.Replace(EnvSettings.DefaultPath, settings.VersionsFile);

        File.WriteAllText(path, content);
        return 0;
    }
}
