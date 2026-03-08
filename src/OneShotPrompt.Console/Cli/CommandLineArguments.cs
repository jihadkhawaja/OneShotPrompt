namespace OneShotPrompt.Console.Cli;

public sealed class CommandLineArguments
{
    public CliCommand Command { get; init; }

    public string ConfigPath { get; init; } = "config.yaml";

    public string? JobName { get; init; }

    public static CommandLineArguments Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return new CommandLineArguments { Command = CliCommand.Run };
        }

        if (args[0] is "help" or "--help" or "-h")
        {
            return new CommandLineArguments { Command = CliCommand.Help };
        }

        if (args[0] is "interactive" or "-i")
        {
            return new CommandLineArguments { Command = CliCommand.Interactive };
        }

        var command = args[0] switch
        {
            "run" => CliCommand.Run,
            "validate" => CliCommand.Validate,
            "jobs" => CliCommand.ListJobs,
            _ => throw new ArgumentException($"Unknown command '{args[0]}'.")
        };

        string configPath = "config.yaml";
        string? jobName = null;

        for (var index = 1; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--config":
                    configPath = GetValue(args, ref index, "--config");
                    break;
                case "--job":
                    jobName = GetValue(args, ref index, "--job");
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{args[index]}'.");
            }
        }

        return new CommandLineArguments
        {
            Command = command,
            ConfigPath = configPath,
            JobName = jobName,
        };
    }

    public static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("OneShotPrompt");
        writer.WriteLine();
        writer.WriteLine("Commands:");
        writer.WriteLine("  run [--config <path>] [--job <name>]");
        writer.WriteLine("  validate [--config <path>]");
        writer.WriteLine("  jobs [--config <path>]");
        writer.WriteLine("  interactive");
        writer.WriteLine("  help");
    }

    private static string GetValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Missing value for {option}.");
        }

        index++;
        return args[index];
    }
}

public enum CliCommand
{
    Help,
    Run,
    Validate,
    ListJobs,
    Interactive,
}