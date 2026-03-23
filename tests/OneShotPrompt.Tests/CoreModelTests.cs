using OneShotPrompt.Application.Abstractions;
using OneShotPrompt.Core.Models;

namespace OneShotPrompt.Tests;

public sealed class CoreModelTests
{
    [Fact]
    public void BuiltInToolCatalog_UsesCaseInsensitiveMatching()
    {
        Assert.True(BuiltInToolCatalog.IsKnown("readtextfile"));
        Assert.False(BuiltInToolCatalog.IsKnown("madeup"));
        Assert.True(BuiltInToolCatalog.RequiresMutation("writeTEXTfile"));
        Assert.False(BuiltInToolCatalog.RequiresMutation("ReadTextFile"));
        Assert.False(BuiltInToolCatalog.RequiresMutation("madeup"));
        Assert.True(BuiltInToolCatalog.IsKnown("MoveFiles"));
        Assert.True(BuiltInToolCatalog.RequiresMutation("movefiles"));
    }

    [Fact]
    public void JobDefinition_ResolvesOptionalOverridesAgainstConfig()
    {
        var config = new AppConfig
        {
            PersistMemory = true,
            ThinkingLevel = "medium",
        };

        var inherited = new JobDefinition();
        var overridden = new JobDefinition
        {
            PersistMemory = false,
            ThinkingLevel = "high",
        };

        Assert.True(inherited.ResolvePersistMemory(config));
        Assert.Equal("medium", inherited.ResolveThinkingLevel(config));
        Assert.Equal("single-agent", inherited.ResolveWorkflow());
        Assert.False(inherited.UsesCorporatePlanning());
        Assert.False(overridden.ResolvePersistMemory(config));
        Assert.Equal("high", overridden.ResolveThinkingLevel(config));

        var planningJob = new JobDefinition
        {
            Workflow = "corporate-planning",
        };

        Assert.True(planningJob.UsesCorporatePlanning());
    }

    [Fact]
    public async Task PreparedJobAgent_ExposesWrappedDependencies()
    {
        var agent = new StubJobAgent("done");
        var summary = new ToolSelectionSummary { SelectedTools = ["ReadTextFile"] };
        var prepared = new PreparedJobAgent(agent, summary);

        Assert.Same(agent, prepared.Agent);
        Assert.Same(summary, prepared.ToolSelection);
        Assert.Equal("done", await prepared.Agent.RunAsync("prompt", CancellationToken.None));

        await prepared.DisposeAsync();

        Assert.True(agent.Disposed);
    }

    private sealed class StubJobAgent(string response) : IJobAgent, IAsyncDisposable
    {
        public bool Disposed { get; private set; }

        public Task<string> RunAsync(string prompt, CancellationToken cancellationToken)
        {
            return Task.FromResult(response);
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}