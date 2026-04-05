using System.Diagnostics.CodeAnalysis;
using OneShotPrompt.Application.Abstractions;
using OneShotPrompt.Application.Services;
using OneShotPrompt.Console.Cli;
using OneShotPrompt.Console.Rendering;
using OneShotPrompt.Core.Models;
using OneShotPrompt.Infrastructure.Configuration;
using OneShotPrompt.Infrastructure.Channels;
using OneShotPrompt.Infrastructure.Logging;
using OneShotPrompt.Infrastructure.Persistence;
using OneShotPrompt.Infrastructure.Providers;

[ExcludeFromCodeCoverage]
internal static class Program
{
    public static Task<int> Main(string[] args)
    {
        if (args.Length == 0 && !Console.IsOutputRedirected && !Console.IsInputRedirected)
        {
            return InteractiveConsoleMenu.RunAsync();
        }

        if (args.Length > 0 && args[0] is "interactive" or "-i")
        {
            return InteractiveConsoleMenu.RunAsync();
        }

        return ConsoleApplication.RunAsync(args, Console.Out, Console.Error);
    }
}

internal static class ConsoleApplication
{
    public static async Task<int> RunAdHocAsync(string configPath, JobDefinition job, TextWriter output, TextWriter error)
    {
        var cancellationSource = new CancellationTokenSource();

        void OnCancelKeyPress(object? _, ConsoleCancelEventArgs eventArgs)
        {
            eventArgs.Cancel = true;
            cancellationSource.Cancel();
        }

        Console.CancelKeyPress += OnCancelKeyPress;

        try
        {
            var configDirectory = Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? Environment.CurrentDirectory;
            var config = await new YamlConfigLoader().LoadAsync(
                configPath,
                cancellationSource.Token,
                new ConfigLoadOptions
                {
                    ProviderValidationScope = ProviderValidationScope.None,
                });

            var sinks = new List<IJobEventSink>
            {
                new FileJobLogger(Path.Combine(configDirectory, "logs"))
            };

            if (ReferenceEquals(output, Console.Out) && !Console.IsOutputRedirected)
            {
                sinks.Add(new SpectreJobEventSink());
            }

            var compositeEventSink = new CompositeJobEventSink([.. sinks]);

            try
            {
                var jobRunner = new JobRunner(
                    new YamlConfigLoader(),
                    new AgentFactory(compositeEventSink),
                    new FileExecutionMemoryStore(),
                    compositeEventSink);

                return await jobRunner.RunAdHocAsync(config, job, configDirectory, output, cancellationSource.Token);
            }
            finally
            {
                await compositeEventSink.DisposeAsync();
            }
        }
        finally
        {
            Console.CancelKeyPress -= OnCancelKeyPress;
            cancellationSource.Dispose();
        }
    }

    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error)
    {
        var cancellationSource = new CancellationTokenSource();

        void OnCancelKeyPress(object? _, ConsoleCancelEventArgs eventArgs)
        {
            eventArgs.Cancel = true;
            cancellationSource.Cancel();
        }

        Console.CancelKeyPress += OnCancelKeyPress;

        try
        {
            CommandLineArguments arguments;

            try
            {
                arguments = CommandLineArguments.Parse(args);
            }
            catch (ArgumentException exception)
            {
                error.WriteLine(exception.Message);
                CommandLineArguments.WriteUsage(error);
                return 1;
            }

            if (arguments.Command is CliCommand.Help)
            {
                CommandLineArguments.WriteUsage(output);
                return 0;
            }

            var configDirectory = Path.GetDirectoryName(Path.GetFullPath(arguments.ConfigPath)) ?? Environment.CurrentDirectory;
            CompositeJobEventSink? compositeEventSink = null;

            if (arguments.Command is CliCommand.Run or CliCommand.Listen)
            {
                var sinks = new List<IJobEventSink>
                {
                    new FileJobLogger(Path.Combine(configDirectory, "logs"))
                };

                if (ReferenceEquals(output, Console.Out) && !Console.IsOutputRedirected)
                {
                    sinks.Add(new SpectreJobEventSink());
                }

                compositeEventSink = new CompositeJobEventSink([.. sinks]);
            }

            try
            {
                var jobRunner = new JobRunner(
                    new YamlConfigLoader(),
                    new AgentFactory(compositeEventSink),
                    new FileExecutionMemoryStore(),
                    compositeEventSink);

                return arguments.Command switch
                {
                    CliCommand.Run => await jobRunner.RunAsync(arguments.ConfigPath, arguments.JobName, output, cancellationSource.Token),
                    CliCommand.Listen => await jobRunner.ListenAsync(
                        arguments.ConfigPath,
                        arguments.JobName!,
                        new WhatsAppPersonalChannelListener(configDirectory).WaitForNextMessageAsync,
                        output,
                        cancellationSource.Token),
                    CliCommand.Validate => await jobRunner.ValidateAsync(arguments.ConfigPath, output, cancellationSource.Token),
                    CliCommand.ListJobs => await jobRunner.ListJobsAsync(arguments.ConfigPath, output, cancellationSource.Token),
                    _ => 1,
                };
            }
            finally
            {
                if (compositeEventSink is not null)
                {
                    await compositeEventSink.DisposeAsync();
                }
            }
        }
        finally
        {
            Console.CancelKeyPress -= OnCancelKeyPress;
            cancellationSource.Dispose();
        }
    }
}
