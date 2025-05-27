using System.ComponentModel;
using Spectre.Console.Cli;

namespace Devlooped;

public class EnvSettings : CommandSettings
{
    public const string DefaultPath = "./.github/workflows/dotnet.json";

    /// <summary>
    /// Optional path to the versions file to use.
    /// </summary>
    [Description("Optional relative path to the versions file to use. If not specified, the default is `./.github/dotnet.json`.")]
    [CommandArgument(0, "[file]")]
    public string VersionsFile { get; set; } = DefaultPath;
}
