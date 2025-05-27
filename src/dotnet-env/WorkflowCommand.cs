using System.ComponentModel;
using System.IO;
using Spectre.Console;
using Spectre.Console.Cli;
using WorkflowSettings = Devlooped.WorkflowCommand.WorkflowSettings;

namespace Devlooped;

[Description("Creates a GitHub Actions workflow file for dotnet-env")]
public class WorkflowCommand : Command<WorkflowSettings>
{
    public class WorkflowSettings : CommandSettings
    {
        /// <summary>
        /// Path to the versions file to use in the workflow.
        /// </summary>
        [Description("Path to the versions file to use in the workflow. Defaults to " + Constants.DefaultPath)]
        [DefaultValue(Constants.DefaultPath)]
        [CommandArgument(0, "[file]")]
        public string VersionsFile { get; set; } = Constants.DefaultPath;
    }

    public override int Execute(CommandContext context, WorkflowSettings settings)
    {
        var path = "./.github/workflows/dotnet-env.yml";
        if (File.Exists(path))
        {
            AnsiConsole.Markup($"[yellow]Workflow file already exists at {path}[/]");
            return 1;
        }

        Directory.CreateDirectory(path);
        var content = ThisAssembly.Resources.dotnet_env.Text;
        if (settings.VersionsFile != Constants.DefaultPath)
            content = content.Replace(Constants.DefaultPath, settings.VersionsFile);

        File.WriteAllText(path, content);
        return 0;
    }
}
