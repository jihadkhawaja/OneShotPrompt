using System.Reflection;
using OneShotPrompt.Core.Enums;
using OneShotPrompt.Core.Models;
using OneShotPrompt.Infrastructure.Providers;

namespace OneShotPrompt.Tests;

public sealed class AgentFactoryReflectionTests
{
    [Fact]
    public async Task CreateAsync_RejectsUnsupportedProviderBeforeCreatingClients()
    {
        var config = new AppConfig();
        var job = new JobDefinition
        {
            Name = "job",
            Prompt = "prompt",
            Provider = "Unsupported",
        };

        var factory = new AgentFactory();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => factory.CreateAsync(config, job, AppContext.BaseDirectory, CancellationToken.None));

        Assert.Equal("Unsupported provider 'Unsupported'.", exception.Message);
    }

    [Fact]
    public async Task CreateAsync_HonorsCancellationEarly()
    {
        var config = new AppConfig();
        var job = new JobDefinition
        {
            Name = "job",
            Prompt = "prompt",
            Provider = "OpenAI",
        };

        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => new AgentFactory().CreateAsync(config, job, AppContext.BaseDirectory, source.Token));
    }

    [Theory]
    [InlineData(JobProvider.OpenAI)]
    [InlineData(JobProvider.OpenAICompatible)]
    [InlineData(JobProvider.Anthropic)]
    public void CreateChatClient_ReturnsClientForSupportedProviders(JobProvider provider)
    {
        var client = ProcessTestHarness.InvokePrivateStatic(typeof(AgentFactory), "CreateChatClient", CreateConfig(), provider);

        Assert.NotNull(client);
    }

    [Fact]
    public async Task CreateAsync_WithSingleAllowedTool_ReturnsPreparedAgentWithoutSelector()
    {
        var config = CreateConfig();
        var job = new JobDefinition
        {
            Name = "inspect",
            Prompt = "Inspect the repository",
            Provider = nameof(JobProvider.OpenAI),
            AutoApprove = false,
        };
        job.AllowedTools.Add("ReadTextFile");

        var prepared = await new AgentFactory().CreateAsync(
            config,
            job,
            Path.Combine(TestPaths.GetSolutionRoot(), "src", "OneShotPrompt.Console"),
            CancellationToken.None);

        Assert.NotNull(prepared.Agent);
        Assert.Equal(5, prepared.ToolSelection.TotalAvailableTools);
        Assert.Equal(1, prepared.ToolSelection.EligibleTools);
        Assert.False(prepared.ToolSelection.SelectorUsed);
        Assert.Equal(["ReadTextFile"], prepared.ToolSelection.AllowedTools);
        Assert.Equal(["ReadTextFile"], prepared.ToolSelection.SelectedTools);
        Assert.Equal("Selector skipped because exactly one eligible tool was available.", prepared.ToolSelection.Rationale);
    }

    [Fact]
    public void ParseToolSelectionResponse_HandlesNoneAndRationale()
    {
        var result = ProcessTestHarness.InvokePrivateStatic(typeof(AgentFactory), "ParseToolSelectionResponse", "RATIONALE: no tools needed\nTOOL: NONE")!;

        Assert.Empty(GetSelectedNames(result));
        Assert.Equal("no tools needed", GetRationale(result));
    }

    [Fact]
    public void ParseToolSelectionResponse_HandlesMultipleToolFormats()
    {
        var result = ProcessTestHarness.InvokePrivateStatic(
            typeof(AgentFactory),
            "ParseToolSelectionResponse",
            "- TOOL: ReadTextFile, ReadTextFileLines\nTOOL: RunCommand\nignored text\nRATIONALE: inspect first")!;

        Assert.Equal(["ReadTextFile", "ReadTextFileLines", "RunCommand"], GetSelectedNames(result).OrderBy(name => name).ToArray());
        Assert.Equal("inspect first", GetRationale(result));
    }

    [Fact]
    public void BuildToolDefinitions_AndAllowlist_WorkAsExpected()
    {
        var inspectionJob = new JobDefinition
        {
            Name = "inspect",
            Prompt = "prompt",
            AutoApprove = false,
        };
        var mutationJob = new JobDefinition
        {
            Name = "mutate",
            Prompt = "prompt",
            AutoApprove = true,
            AllowedTools = { "ReadTextFile", "RunCommand" },
        };

        var inspectionTools = GetToolNames(ProcessTestHarness.InvokePrivateStatic(typeof(AgentFactory), "BuildToolDefinitions", inspectionJob, AppContext.BaseDirectory)!);
        var mutationToolsObject = ProcessTestHarness.InvokePrivateStatic(typeof(AgentFactory), "BuildToolDefinitions", mutationJob, AppContext.BaseDirectory)!;
        var mutationTools = GetToolNames(mutationToolsObject);
        var filtered = GetToolNames(ProcessTestHarness.InvokePrivateStatic(typeof(AgentFactory), "ApplyAllowlist", mutationJob, mutationToolsObject)!);

        Assert.Equal(5, inspectionTools.Count);
        Assert.Equal(12, mutationTools.Count);
        Assert.DoesNotContain("WriteTextFile", inspectionTools);
        Assert.Contains("WriteTextFile", mutationTools);
        Assert.Equal(["ReadTextFile", "RunCommand"], filtered.OrderBy(name => name).ToArray());
    }

    [Fact]
    public void BuildInstructionAndPromptHelpers_ExposeExpectedContext()
    {
        var config = new AppConfig { ThinkingLevel = "medium" };
        config.OpenAI.ApiKey = "key";
        config.OpenAI.Model = "model";

        var job = new JobDefinition
        {
            Name = "nightly",
            Prompt = "Inspect the repo",
            Provider = "OpenAI",
            AutoApprove = true,
            AllowedTools = { "ReadTextFile" },
        };

        var availableTools = ProcessTestHarness.InvokePrivateStatic(typeof(AgentFactory), "BuildToolDefinitions", job, AppContext.BaseDirectory)!;
        var instructions = (string)ProcessTestHarness.InvokePrivateStatic(typeof(AgentFactory), "BuildExecutionInstructions", config, job, 12, availableTools, (IReadOnlyList<string>)job.AllowedTools)!;
        var selectorInstructions = (string)ProcessTestHarness.InvokePrivateStatic(typeof(AgentFactory), "BuildToolSelectionInstructions", config, job, 12)!;
        var prompt = (string)ProcessTestHarness.InvokePrivateStatic(typeof(AgentFactory), "BuildToolSelectionPrompt", config, job, availableTools)!;

        Assert.Contains("Configured tool allowlist: ReadTextFile", instructions);
        Assert.Contains("Selected tools for this run:", instructions);
        Assert.Contains("Mutation tools are available only if they survived the selector pass.", instructions);
        Assert.Contains("Available tool count: 12.", selectorInstructions);
        Assert.Contains("Return one selected tool per line using exactly: TOOL: <tool-name>.", selectorInstructions);
        Assert.Contains("Job: nightly", prompt);
        Assert.Contains("Task:", prompt);
        Assert.Contains("Available tools:", prompt);
    }

    [Fact]
    public void BuildInstructionHelpers_ExposeNonMutationFallbacks()
    {
        var config = CreateConfig();
        var job = new JobDefinition
        {
            Name = "inspect",
            Prompt = "Look only",
            Provider = nameof(JobProvider.OpenAI),
            AutoApprove = false,
        };

        var availableTools = ProcessTestHarness.InvokePrivateStatic(typeof(AgentFactory), "BuildToolDefinitions", job, AppContext.BaseDirectory)!;
        var emptySelectedJob = new JobDefinition
        {
            Name = "none",
            Prompt = "none",
            Provider = nameof(JobProvider.OpenAI),
            AutoApprove = false,
        };
        emptySelectedJob.AllowedTools.Add("RunCommand");
        var emptySelectedTools = ProcessTestHarness.InvokePrivateStatic(typeof(AgentFactory), "ApplyAllowlist", emptySelectedJob, availableTools)!;

        var instructions = (string)ProcessTestHarness.InvokePrivateStatic(typeof(AgentFactory), "BuildExecutionInstructions", config, job, 5, emptySelectedTools, Array.Empty<string>())!;
        var selectorInstructions = (string)ProcessTestHarness.InvokePrivateStatic(typeof(AgentFactory), "BuildToolSelectionInstructions", config, job, 5)!;
        var prompt = (string)ProcessTestHarness.InvokePrivateStatic(typeof(AgentFactory), "BuildToolSelectionPrompt", config, job, availableTools)!;

        Assert.Contains("Configured tool allowlist: none", instructions);
        Assert.Contains("Selected tools for this run: none", instructions);
        Assert.Contains("Mutation tools are not available.", instructions);
        Assert.Contains("Mutation tools are unavailable, so prefer inspection-only planning.", selectorInstructions);
        Assert.Contains("Mutation tools available: no", prompt);
    }

    [Fact]
    public async Task SelectToolsAsync_ReturnsFallbacksForZeroAndSingleEligibleTools()
    {
        var config = CreateConfig();

        var zeroJob = new JobDefinition
        {
            Name = "zero",
            Prompt = "none",
            Provider = nameof(JobProvider.OpenAI),
            AutoApprove = false,
        };
        zeroJob.AllowedTools.Add("RunCommand");

        var zeroAvailable = ProcessTestHarness.InvokePrivateStatic(typeof(AgentFactory), "BuildToolDefinitions", zeroJob, AppContext.BaseDirectory)!;
        var zeroEligible = ProcessTestHarness.InvokePrivateStatic(typeof(AgentFactory), "ApplyAllowlist", zeroJob, zeroAvailable)!;
        var zeroDecision = await ProcessTestHarness.InvokePrivateStaticAsync(
            typeof(AgentFactory),
            "SelectToolsAsync",
            null,
            config,
            zeroJob,
            AppContext.BaseDirectory,
            zeroEligible,
            CancellationToken.None) ?? throw new InvalidOperationException("Expected a tool selection decision.");

        Assert.False(GetSelectorUsed(zeroDecision));
        Assert.Empty(GetSelectedToolNames(zeroDecision));
        Assert.Equal("No eligible tools remained after allowlist filtering.", GetRationale(zeroDecision));

        var oneJob = new JobDefinition
        {
            Name = "one",
            Prompt = "single",
            Provider = nameof(JobProvider.OpenAI),
            AutoApprove = false,
        };
        oneJob.AllowedTools.Add("ReadTextFile");

        var oneAvailable = ProcessTestHarness.InvokePrivateStatic(typeof(AgentFactory), "BuildToolDefinitions", oneJob, AppContext.BaseDirectory)!;
        var oneEligible = ProcessTestHarness.InvokePrivateStatic(typeof(AgentFactory), "ApplyAllowlist", oneJob, oneAvailable)!;
        var oneDecision = await ProcessTestHarness.InvokePrivateStaticAsync(
            typeof(AgentFactory),
            "SelectToolsAsync",
            null,
            config,
            oneJob,
            AppContext.BaseDirectory,
            oneEligible,
            CancellationToken.None) ?? throw new InvalidOperationException("Expected a tool selection decision.");

        Assert.False(GetSelectorUsed(oneDecision));
        Assert.Equal(["ReadTextFile"], GetSelectedToolNames(oneDecision));
        Assert.Equal("Selector skipped because exactly one eligible tool was available.", GetRationale(oneDecision));
    }

    [Fact]
    public void GetSkillPaths_IncludesWorkspaceSkillsDirectoryWhenPresent()
    {
        using var workspace = new TestWorkspace();
        var skillsPath = workspace.GetPath("skills");
        Directory.CreateDirectory(skillsPath);

        var skillPaths = (List<string>)ProcessTestHarness.InvokePrivateStatic(typeof(AgentFactory), "GetSkillPaths", workspace.RootPath)!;

        Assert.Contains(skillsPath, skillPaths);
    }

    private static string? GetRationale(object parseResult)
    {
        return (string?)parseResult.GetType().GetProperty("Rationale")!.GetValue(parseResult);
    }

    private static bool GetSelectorUsed(object toolSelectionDecision)
    {
        return (bool)toolSelectionDecision.GetType().GetProperty("SelectorUsed")!.GetValue(toolSelectionDecision)!;
    }

    private static HashSet<string> GetSelectedNames(object parseResult)
    {
        return (HashSet<string>)parseResult.GetType().GetProperty("SelectedNames")!.GetValue(parseResult)!;
    }

    private static List<string> GetSelectedToolNames(object toolSelectionDecision)
    {
        return ((IEnumerable<object>)toolSelectionDecision.GetType().GetProperty("SelectedTools")!.GetValue(toolSelectionDecision)!)
            .Select(item => (string)item.GetType().GetProperty("Name")!.GetValue(item)!)
            .ToList();
    }

    private static List<string> GetToolNames(object toolDefinitions)
    {
        return ((IEnumerable<object>)toolDefinitions)
            .Select(item => (string)item.GetType().GetProperty("Name")!.GetValue(item)!)
            .ToList();
    }

    private static AppConfig CreateConfig()
    {
        return new AppConfig
        {
            ThinkingLevel = "medium",
            OpenAI =
            {
                ApiKey = "key",
                Model = "gpt-test",
            },
            Anthropic =
            {
                ApiKey = "key",
                Model = "claude-test",
            },
            OpenAICompatible =
            {
                Endpoint = "http://localhost:1234/v1",
                ApiKey = "compat-key",
                Model = "compat-model",
            },
        };
    }
}