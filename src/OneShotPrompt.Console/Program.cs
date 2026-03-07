using OneShotPrompt.Application.Services;
using OneShotPrompt.Console.Cli;
using OneShotPrompt.Infrastructure.Configuration;
using OneShotPrompt.Infrastructure.Persistence;
using OneShotPrompt.Infrastructure.Providers;
using System.Diagnostics.CodeAnalysis;

[ExcludeFromCodeCoverage]
internal static class Program
{
	public static Task<int> Main(string[] args)
	{
		return ConsoleApplication.RunAsync(args, Console.Out, Console.Error);
	}
}

internal static class ConsoleApplication
{
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

			var jobRunner = new JobRunner(
				new YamlConfigLoader(),
				new AgentFactory(),
				new FileExecutionMemoryStore());

			return arguments.Command switch
			{
				CliCommand.Run => await jobRunner.RunAsync(arguments.ConfigPath, arguments.JobName, output, cancellationSource.Token),
				CliCommand.Validate => await jobRunner.ValidateAsync(arguments.ConfigPath, output, cancellationSource.Token),
				CliCommand.ListJobs => await jobRunner.ListJobsAsync(arguments.ConfigPath, output, cancellationSource.Token),
				_ => 1,
			};
		}
		finally
		{
			Console.CancelKeyPress -= OnCancelKeyPress;
			cancellationSource.Dispose();
		}
	}
}
