using System.Diagnostics.CodeAnalysis;
using OneShotPrompt.Core.Enums;
using OneShotPrompt.Core.Models;
using OneShotPrompt.Infrastructure.Configuration;
using Spectre.Console;

namespace OneShotPrompt.Console.Rendering;

[ExcludeFromCodeCoverage]
internal static class InteractiveConsoleMenu
{
    public static async Task<int> RunAsync()
    {
        AnsiConsole.Write(new Rule("[bold yellow]OneShotPrompt[/]").RuleStyle("dim"));
        AnsiConsole.WriteLine();

        var configPath = AnsiConsole.Prompt(
            new TextPrompt<string>("[blue]Config file:[/]")
                .DefaultValue("config.yaml"));

        if (!File.Exists(configPath))
        {
            AnsiConsole.MarkupLine($"[red]Config not found:[/] {Markup.Escape(configPath)}");
            return 1;
        }

        var lastExitCode = 0;

        while (true)
        {
            AnsiConsole.WriteLine();

            var command = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[blue]Select action:[/]")
                    .HighlightStyle("yellow")
                    .AddChoices("Run direct prompt", "Run all jobs", "Run specific job", "Validate", "List jobs", "Clear memories", "Exit"));

            if (command == "Exit")
            {
                break;
            }

            lastExitCode = command switch
            {
                "Run direct prompt" => await RunDirectPromptAsync(configPath),
                "Run all jobs" => await ConsoleApplication.RunAsync(
                    ["run", "--config", configPath], System.Console.Out, System.Console.Error),
                "Run specific job" => await SelectAndRunJobAsync(configPath),
                "Validate" => await ConsoleApplication.RunAsync(
                    ["validate", "--config", configPath], System.Console.Out, System.Console.Error),
                "List jobs" => await ConsoleApplication.RunAsync(
                    ["jobs", "--config", configPath], System.Console.Out, System.Console.Error),
                "Clear memories" => ClearMemories(configPath),
                _ => 1,
            };
        }

        return lastExitCode;
    }

    private static async Task<int> RunDirectPromptAsync(string configPath)
    {
        var providers = Enum.GetNames<JobProvider>();

        var provider = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[blue]Provider:[/]")
                .HighlightStyle("yellow")
                .AddChoices(providers));

        var autoApprove = AnsiConsole.Confirm("[blue]Allow mutation tools?[/]", false);

        var lastExitCode = 0;

        while (true)
        {
            var prompt = AnsiConsole.Prompt(
                new TextPrompt<string>("[blue]Prompt[/] [grey](empty to go back)[/][blue]:[/]")
                    .AllowEmpty());

            if (string.IsNullOrWhiteSpace(prompt))
            {
                break;
            }

            var job = new JobDefinition
            {
                Name = "ad-hoc",
                Prompt = prompt,
                Provider = provider,
                AutoApprove = autoApprove,
                Enabled = true,
            };

            lastExitCode = await ConsoleApplication.RunAdHocAsync(configPath, job, System.Console.Out, System.Console.Error);
            AnsiConsole.WriteLine();
        }

        return lastExitCode;
    }

    private static int ClearMemories(string configPath)
    {
        var configDirectory = Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? Environment.CurrentDirectory;
        var memoryDirectory = Path.Combine(configDirectory, ".oneshotprompt", "memory");

        if (!Directory.Exists(memoryDirectory))
        {
            AnsiConsole.MarkupLine("[yellow]No memories found.[/]");
            return 0;
        }

        var files = Directory.GetFiles(memoryDirectory, "*.json");

        if (files.Length == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No memories found.[/]");
            return 0;
        }

        AnsiConsole.MarkupLine($"[yellow]Found {files.Length} memory file(s).[/]");

        if (!AnsiConsole.Confirm("[red]Delete all memory files?[/]", false))
        {
            AnsiConsole.MarkupLine("[grey]Cancelled.[/]");
            return 0;
        }

        foreach (var file in files)
        {
            File.Delete(file);
        }

        AnsiConsole.MarkupLine("[green]Memories cleared.[/]");
        return 0;
    }

    private static async Task<int> SelectAndRunJobAsync(string configPath)
    {
        var loader = new YamlConfigLoader();
        var config = await loader.LoadAsync(
            configPath,
            CancellationToken.None,
            new ConfigLoadOptions
            {
                ProviderValidationScope = ProviderValidationScope.None,
            });
        var enabledJobs = config.Jobs
            .Where(job => job.Enabled)
            .Select(job => job.Name)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (enabledJobs.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]No enabled jobs found.[/]");
            return 1;
        }

        var selectedJob = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[blue]Select job:[/]")
                .HighlightStyle("yellow")
                .AddChoices(enabledJobs));

        return await ConsoleApplication.RunAsync(
            ["run", "--config", configPath, "--job", selectedJob], System.Console.Out, System.Console.Error);
    }
}
