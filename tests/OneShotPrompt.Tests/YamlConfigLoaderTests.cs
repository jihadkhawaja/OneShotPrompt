using OneShotPrompt.Infrastructure.Configuration;

namespace OneShotPrompt.Tests;

public sealed class YamlConfigLoaderTests
{
    private readonly YamlConfigLoader _loader = new();

    [Fact]
    public async Task LoadAsync_ParsesValidConfiguration()
    {
        using var workspace = new TestWorkspace();
        var path = workspace.WriteFile("config.yaml", """
            ThinkingLevel: high
            PersistMemory: false
            OpenAI:
              ApiKey: openai-key
              Model: gpt-test
            Anthropic:
              ApiKey: anthropic-key
              Model: claude-test
            OpenAICompatible:
              Endpoint: http://localhost:1234/v1
              ApiKey: compat-key
              Model: compat-model
            GitHubCopilot:
              Model: gpt-5
              CliPath: C:/tools/copilot.exe
              LogLevel: debug
              AutoStart: false
              AutoRestart: false
            Jobs:
              - Name: Example
                Prompt: "Do useful work"
                Provider: OpenAI
                AutoApprove: true
                PersistMemory: true
                ThinkingLevel: medium
                Schedule: daily
                Enabled: true
                AllowedTools: [ReadTextFile, WriteTextFile]
            """);

        var config = await _loader.LoadAsync(path, CancellationToken.None);

        Assert.Equal("high", config.ThinkingLevel);
        Assert.False(config.PersistMemory);
        Assert.Equal("openai-key", config.OpenAI.ApiKey);
        Assert.Equal("claude-test", config.Anthropic.Model);
        Assert.Equal("http://localhost:1234/v1", config.OpenAICompatible.Endpoint);
        Assert.Equal("gpt-5", config.GitHubCopilot.Model);
        Assert.Equal("C:/tools/copilot.exe", config.GitHubCopilot.CliPath);
        Assert.Equal("debug", config.GitHubCopilot.LogLevel);
        Assert.False(config.GitHubCopilot.AutoStart);
        Assert.False(config.GitHubCopilot.AutoRestart);

        var job = Assert.Single(config.Jobs);
        Assert.Equal("Example", job.Name);
        Assert.Equal("Do useful work", job.Prompt);
        Assert.Equal("OpenAI", job.Provider);
        Assert.True(job.AutoApprove);
        Assert.True(job.PersistMemory);
        Assert.Equal("medium", job.ThinkingLevel);
        Assert.Equal("daily", job.Schedule);
        Assert.True(job.Enabled);
        Assert.Equal(["ReadTextFile", "WriteTextFile"], job.AllowedTools);
    }

    [Fact]
    public async Task LoadAsync_MissingFile_Throws()
    {
        using var workspace = new TestWorkspace();
        var path = workspace.GetPath("missing.yaml");

        var exception = await Assert.ThrowsAsync<FileNotFoundException>(() => _loader.LoadAsync(path, CancellationToken.None));

        Assert.Equal(path, exception.FileName);
    }

    [Fact]
    public async Task LoadAsync_RequiresAtLeastOneJob()
    {
        var exception = await LoadInvalidConfigAsync("""
            OpenAI:
              ApiKey: key
              Model: model
            """);

        Assert.Contains("must define at least one job", exception.Message);
    }

    [Fact]
    public async Task LoadAsync_RejectsDuplicateJobNames()
    {
        var exception = await LoadInvalidConfigAsync("""
            OpenAI:
              ApiKey: key
              Model: model
            Jobs:
              - Name: Daily
                Prompt: first
                Provider: OpenAI
              - Name: daily
                Prompt: second
                Provider: OpenAI
            """);

        Assert.Contains("defined more than once", exception.Message);
    }

    [Fact]
    public async Task LoadAsync_RejectsMissingJobName()
    {
        var exception = await LoadInvalidConfigAsync("""
            OpenAI:
              ApiKey: key
              Model: model
            Jobs:
              - Prompt: first
                Provider: OpenAI
            """);

        Assert.Equal("Each job must define Name.", exception.Message);
    }

    [Fact]
    public async Task LoadAsync_RejectsMissingPrompt()
    {
        var exception = await LoadInvalidConfigAsync("""
            OpenAI:
              ApiKey: key
              Model: model
            Jobs:
              - Name: Daily
                Provider: OpenAI
            """);

        Assert.Equal("Job 'Daily' must define Prompt.", exception.Message);
    }

    [Fact]
    public async Task LoadAsync_RejectsUnsupportedProvider()
    {
        var exception = await LoadInvalidConfigAsync("""
            OpenAI:
              ApiKey: key
              Model: model
            Jobs:
              - Name: Daily
                Prompt: first
                Provider: Unknown
            """);

        Assert.Equal("Job 'Daily' uses unsupported provider 'Unknown'.", exception.Message);
    }

    [Fact]
    public async Task LoadAsync_RejectsInvalidThinkingLevels()
    {
        var rootException = await LoadInvalidConfigAsync("""
            ThinkingLevel: extreme
            OpenAI:
              ApiKey: key
              Model: model
            Jobs:
              - Name: Daily
                Prompt: first
                Provider: OpenAI
            """);

        Assert.Equal("'ThinkingLevel' must be one of: low, medium, high.", rootException.Message);

        var jobException = await LoadInvalidConfigAsync("""
            OpenAI:
              ApiKey: key
              Model: model
            Jobs:
              - Name: Daily
                Prompt: first
                Provider: OpenAI
                ThinkingLevel: extreme
            """);

        Assert.Equal("'Jobs[Daily].ThinkingLevel' must be one of: low, medium, high.", jobException.Message);
    }

    [Fact]
    public async Task LoadAsync_RejectsUnknownAndMutationRestrictedAllowedTools()
    {
        var unknownTool = await LoadInvalidConfigAsync("""
            OpenAI:
              ApiKey: key
              Model: model
            Jobs:
              - Name: Daily
                Prompt: first
                Provider: OpenAI
                AllowedTools: [UnknownTool]
            """);

        Assert.Equal("Job 'Daily' uses unknown AllowedTools entry 'UnknownTool'.", unknownTool.Message);

        var mutationTool = await LoadInvalidConfigAsync("""
            OpenAI:
              ApiKey: key
              Model: model
            Jobs:
              - Name: Daily
                Prompt: first
                Provider: OpenAI
                AutoApprove: false
                AllowedTools: [WriteTextFile]
            """);

        Assert.Equal("Job 'Daily' cannot allow mutation tool 'WriteTextFile' when AutoApprove is false.", mutationTool.Message);
    }

    [Fact]
    public async Task LoadAsync_RejectsMissingProviderSettings()
    {
        var openAi = await LoadInvalidConfigAsync("""
            OpenAI:
              ApiKey: 
              Model: model
            Jobs:
              - Name: Daily
                Prompt: first
                Provider: OpenAI
            """);

        Assert.Equal("Job 'Daily' requires 'OpenAI.ApiKey' to be configured.", openAi.Message);

        var anthropic = await LoadInvalidConfigAsync("""
            Anthropic:
              ApiKey: key
              Model: 
            Jobs:
              - Name: Daily
                Prompt: first
                Provider: Anthropic
            """);

        Assert.Equal("Job 'Daily' requires 'Anthropic.Model' to be configured.", anthropic.Message);

        var compatible = await LoadInvalidConfigAsync("""
            OpenAICompatible:
              Endpoint: 
              ApiKey: compat
              Model: compat-model
            Jobs:
              - Name: Daily
                Prompt: first
                Provider: OpenAICompatible
            """);

        Assert.Equal("Job 'Daily' requires 'OpenAICompatible.Endpoint' to be configured.", compatible.Message);
    }

    [Fact]
    public async Task LoadAsync_RejectsInvalidGitHubCopilotCombinations()
    {
        var bothConnectionModes = await LoadInvalidConfigAsync("""
            GitHubCopilot:
              CliPath: C:/tools/copilot.exe
              CliUrl: http://localhost:3000
            Jobs:
              - Name: Daily
                Prompt: first
                Provider: GitHubCopilot
            """);

        Assert.Equal("Job 'Daily' cannot set both 'GitHubCopilot.CliUrl' and 'GitHubCopilot.CliPath'.", bothConnectionModes.Message);

        var externalServerWithAuth = await LoadInvalidConfigAsync("""
            GitHubCopilot:
              CliUrl: http://localhost:3000
              GitHubToken: token
            Jobs:
              - Name: Daily
                Prompt: first
                Provider: GitHubCopilot
            """);

        Assert.Equal("Job 'Daily' cannot combine 'GitHubCopilot.CliUrl' with 'GitHubCopilot.GitHubToken' or 'GitHubCopilot.UseLoggedInUser'.", externalServerWithAuth.Message);
    }

    [Fact]
    public async Task LoadAsync_RejectsUnsupportedSectionsAndMalformedInput()
    {
        var unsupportedSection = await LoadInvalidConfigAsync("""
            Unsupported:
              Value: test
            OpenAI:
              ApiKey: key
              Model: model
            Jobs:
              - Name: Daily
                Prompt: first
                Provider: OpenAI
            """);

        Assert.Equal("Unsupported YAML section 'Unsupported'.", unsupportedSection.Message);

        var malformedJob = await LoadInvalidConfigAsync("""
            OpenAI:
              ApiKey: key
              Model: model
            Jobs:
              Name: Daily
            """);

        Assert.Equal("Job entries must start with '- '.", malformedJob.Message);

        var invalidIndent = await LoadInvalidConfigAsync("""
             OpenAI:
              ApiKey: key
              Model: model
            Jobs:
              - Name: Daily
                Prompt: first
                Provider: OpenAI
            """);

        Assert.Equal("Invalid indentation for root entry. Expected 0 spaces but found 1.", invalidIndent.Message);

        var invalidBoolean = await LoadInvalidConfigAsync("""
            PersistMemory: maybe
            OpenAI:
              ApiKey: key
              Model: model
            Jobs:
              - Name: Daily
                Prompt: first
                Provider: OpenAI
            """);

        Assert.Equal("'PersistMemory' must be a boolean value.", invalidBoolean.Message);
    }

    private async Task<InvalidOperationException> LoadInvalidConfigAsync(string yaml)
    {
        using var workspace = new TestWorkspace();
        var path = workspace.WriteFile("config.yaml", yaml);
        return await Assert.ThrowsAsync<InvalidOperationException>(() => _loader.LoadAsync(path, CancellationToken.None));
    }
}