// See https://aka.ms/new-console-template for more information
using System;
using Devlooped;
using Spectre.Console.Cli;

var app = new CommandApp();

app.Configure(config =>
{
    if (ThisAssembly.Project.ToolCommandName.StartsWith("dotnet-"))
        config.SetApplicationName("dotnet " + ThisAssembly.Project.ToolCommandName.Replace("dotnet-", ""));
    else
        config.SetApplicationName(ThisAssembly.Project.ToolCommandName);

    config.AddCommand<InitCommand>("init");
    config.AddCommand<WorkflowCommand>("workflow");

    // Allows emitting help markdown on build
    if (Environment.GetEnvironmentVariables().Contains("NO_COLOR"))
        config.Settings.HelpProviderStyles = null;
});

await app.RunAsync(args);
