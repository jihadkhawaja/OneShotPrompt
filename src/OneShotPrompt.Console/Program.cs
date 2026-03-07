using OneShotPrompt.Application.Services;
using OneShotPrompt.Console.Cli;
using OneShotPrompt.Infrastructure.Configuration;
using OneShotPrompt.Infrastructure.Persistence;
using OneShotPrompt.Infrastructure.Providers;

var cancellationSource = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
	eventArgs.Cancel = true;
	cancellationSource.Cancel();
};

CommandLineArguments arguments;

try
{
	arguments = CommandLineArguments.Parse(args);
}
catch (ArgumentException exception)
{
	Console.Error.WriteLine(exception.Message);
	CommandLineArguments.WriteUsage(Console.Error);
	return 1;
}

if (arguments.Command is CliCommand.Help)
{
	CommandLineArguments.WriteUsage(Console.Out);
	return 0;
}

var jobRunner = new JobRunner(
	new YamlConfigLoader(),
	new AgentFactory(),
	new FileExecutionMemoryStore());

return arguments.Command switch
{
	CliCommand.Run => await jobRunner.RunAsync(arguments.ConfigPath, arguments.JobName, Console.Out, cancellationSource.Token),
	CliCommand.Validate => await jobRunner.ValidateAsync(arguments.ConfigPath, Console.Out, cancellationSource.Token),
	CliCommand.ListJobs => await jobRunner.ListJobsAsync(arguments.ConfigPath, Console.Out, cancellationSource.Token),
	_ => 1,
};
