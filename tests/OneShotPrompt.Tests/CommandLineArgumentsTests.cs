using OneShotPrompt.Console.Cli;

namespace OneShotPrompt.Tests;

public sealed class CommandLineArgumentsTests
{
    [Fact]
    public void Parse_WithoutArguments_UsesRunDefaults()
    {
        var arguments = CommandLineArguments.Parse([]);

        Assert.Equal(CliCommand.Run, arguments.Command);
        Assert.Equal("config.yaml", arguments.ConfigPath);
        Assert.Null(arguments.JobName);
    }

    [Theory]
    [InlineData("help")]
    [InlineData("--help")]
    [InlineData("-h")]
    public void Parse_HelpAliases_ReturnHelpCommand(string command)
    {
        var arguments = CommandLineArguments.Parse([command]);

        Assert.Equal(CliCommand.Help, arguments.Command);
    }

    [Fact]
    public void Parse_RunCommandWithOptions_ReturnsExpectedValues()
    {
        var arguments = CommandLineArguments.Parse(["run", "--config", "custom.yaml", "--job", "nightly"]);

        Assert.Equal(CliCommand.Run, arguments.Command);
        Assert.Equal("custom.yaml", arguments.ConfigPath);
        Assert.Equal("nightly", arguments.JobName);
    }

    [Fact]
    public void Parse_ListenCommandWithOptions_ReturnsExpectedValues()
    {
        var arguments = CommandLineArguments.Parse(["listen", "--config", "custom.yaml", "--job", "personal-whatsapp-reply"]);

        Assert.Equal(CliCommand.Listen, arguments.Command);
        Assert.Equal("custom.yaml", arguments.ConfigPath);
        Assert.Equal("personal-whatsapp-reply", arguments.JobName);
    }

    [Theory]
    [InlineData("validate", CliCommand.Validate)]
    [InlineData("jobs", CliCommand.ListJobs)]
    public void Parse_OtherKnownCommands_AreRecognized(string command, CliCommand expected)
    {
        var arguments = CommandLineArguments.Parse([command, "--config", "config.custom.yaml"]);

        Assert.Equal(expected, arguments.Command);
        Assert.Equal("config.custom.yaml", arguments.ConfigPath);
    }

    [Fact]
    public void Parse_UnknownCommand_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => CommandLineArguments.Parse(["deploy"]));

        Assert.Equal("Unknown command 'deploy'.", exception.Message);
    }

    [Fact]
    public void Parse_UnknownOption_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => CommandLineArguments.Parse(["run", "--unknown"]));

        Assert.Equal("Unknown option '--unknown'.", exception.Message);
    }

    [Fact]
    public void Parse_ListenWithoutJob_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => CommandLineArguments.Parse(["listen", "--config", "custom.yaml"]));

        Assert.Equal("The listen command requires --job <name>.", exception.Message);
    }

    [Fact]
    public void Parse_MissingOptionValue_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => CommandLineArguments.Parse(["run", "--config"]));

        Assert.Equal("Missing value for --config.", exception.Message);
    }

    [Theory]
    [InlineData("interactive")]
    [InlineData("-i")]
    public void Parse_InteractiveAliases_ReturnInteractiveCommand(string command)
    {
        var arguments = CommandLineArguments.Parse([command]);

        Assert.Equal(CliCommand.Interactive, arguments.Command);
    }

    [Fact]
    public void WriteUsage_WritesExpectedCommands()
    {
        using var writer = new StringWriter();

        CommandLineArguments.WriteUsage(writer);

        var usage = writer.ToString();
        Assert.Contains("OneShotPrompt", usage);
        Assert.Contains("run [--config <path>] [--job <name>]", usage);
        Assert.Contains("listen [--config <path>] --job <name>", usage);
        Assert.Contains("validate [--config <path>]", usage);
        Assert.Contains("jobs [--config <path>]", usage);
        Assert.Contains("interactive", usage);
        Assert.Contains("help", usage);
    }
}