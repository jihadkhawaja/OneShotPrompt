namespace OneShotPrompt.Tests;

public sealed class ProgramIntegrationTests
{
    [Fact]
    public async Task ConsoleApplication_Help_PrintsUsageAndReturnsZero()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await ConsoleApplication.RunAsync(["help"], output, error);

        Assert.Equal(0, exitCode);
        Assert.Contains("Commands:", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task ConsoleApplication_InvalidCommand_PrintsErrorAndUsage()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await ConsoleApplication.RunAsync(["deploy"], output, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown command 'deploy'.", error.ToString());
        Assert.Contains("Commands:", error.ToString());
    }

    [Fact]
    public async Task ConsoleApplication_Validate_UsesRealComposition()
    {
        using var workspace = new TestWorkspace();
        var configPath = workspace.WriteFile("config.yaml", """
            OpenAI:
              ApiKey: key
              Model: model
            Jobs:
              - Name: Daily
                Prompt: Inspect the repository
                Provider: OpenAI
            """);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await ConsoleApplication.RunAsync(["validate", "--config", configPath], output, error);

        Assert.Equal(0, exitCode);
        Assert.Contains("Configuration is valid. Jobs: 1.", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task ConsoleApplication_Jobs_ListsConfiguredJobs()
    {
        using var workspace = new TestWorkspace();
        var configPath = workspace.WriteFile("config.yaml", """
            OpenAI:
              ApiKey: key
              Model: model
            Jobs:
              - Name: Daily
                Prompt: Inspect the repository
                Provider: OpenAI
                Enabled: false
                Schedule: daily
            """);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await ConsoleApplication.RunAsync(["jobs", "--config", configPath], output, error);

        Assert.Equal(0, exitCode);
        Assert.Contains("- Daily | Provider=OpenAI | Enabled=False | Schedule=daily", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task ConsoleApplication_RunWithoutEnabledJobs_ReturnsOne()
    {
        using var workspace = new TestWorkspace();
        var configPath = workspace.WriteFile("config.yaml", """
            OpenAI:
              ApiKey: key
              Model: model
            Jobs:
              - Name: Daily
                Prompt: Inspect the repository
                Provider: OpenAI
                Enabled: false
            """);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await ConsoleApplication.RunAsync(["run", "--config", configPath], output, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("No enabled jobs were found in the configuration.", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task Program_Help_PrintsUsageAndReturnsZero()
    {
        var result = await ProcessTestHarness.RunConsoleAsync("help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Commands:", result.StandardOutput);
        Assert.True(string.IsNullOrWhiteSpace(result.StandardError));
    }

    [Fact]
    public async Task Program_InvalidCommand_PrintsErrorAndUsage()
    {
        var result = await ProcessTestHarness.RunConsoleAsync("deploy");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Unknown command 'deploy'.", result.StandardError);
        Assert.Contains("Commands:", result.StandardError);
    }

    [Fact]
    public async Task Program_Validate_UsesRealComposition()
    {
        using var workspace = new TestWorkspace();
        var configPath = workspace.WriteFile("config.yaml", """
            OpenAI:
              ApiKey: key
              Model: model
            Jobs:
              - Name: Daily
                Prompt: Inspect the repository
                Provider: OpenAI
            """);

        var result = await ProcessTestHarness.RunConsoleAsync("validate", "--config", configPath);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Configuration is valid. Jobs: 1.", result.StandardOutput);
    }

    [Fact]
    public async Task Program_Jobs_ListsConfiguredJobs()
    {
        using var workspace = new TestWorkspace();
        var configPath = workspace.WriteFile("config.yaml", """
            OpenAI:
              ApiKey: key
              Model: model
            Jobs:
              - Name: Daily
                Prompt: Inspect the repository
                Provider: OpenAI
                Enabled: false
                Schedule: daily
            """);

        var result = await ProcessTestHarness.RunConsoleAsync("jobs", "--config", configPath);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("- Daily | Provider=OpenAI | Enabled=False | Schedule=daily", result.StandardOutput);
    }

    [Fact]
    public async Task Program_RunWithoutEnabledJobs_ReturnsOneWithoutCallingProviders()
    {
        using var workspace = new TestWorkspace();
        var configPath = workspace.WriteFile("config.yaml", """
            OpenAI:
              ApiKey: key
              Model: model
            Jobs:
              - Name: Daily
                Prompt: Inspect the repository
                Provider: OpenAI
                Enabled: false
            """);

        var result = await ProcessTestHarness.RunConsoleAsync("run", "--config", configPath);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("No enabled jobs were found in the configuration.", result.StandardOutput);
    }
}