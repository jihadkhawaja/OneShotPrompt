using OneShotPrompt.Application.Abstractions;
using OneShotPrompt.Application.Services;
using OneShotPrompt.Core.Models;

namespace OneShotPrompt.Tests;

public sealed class JobRunnerTests
{
    [Fact]
    public async Task RunAsync_ReturnsOneWhenNoEnabledJobMatches()
    {
        var config = CreateConfig(new JobDefinition
        {
            Name = "disabled",
            Prompt = "noop",
            Provider = "OpenAI",
            Enabled = false,
        });

        var runner = new JobRunner(
            new FakeConfigLoader(config),
            new FakeJobAgentFactory(),
            new FakeExecutionMemoryStore());

        using var writer = new StringWriter();
        var exitCode = await runner.RunAsync("config.yaml", "missing", writer, CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains("No enabled job named 'missing' was found.", writer.ToString());
    }

    [Fact]
    public async Task RunAsync_ExecutesJobWithoutPersistence_WhenDisabled()
    {
        var config = CreateConfig(new JobDefinition
        {
            Name = "inspect",
            Prompt = "Review the repository",
            Provider = "OpenAI",
            AutoApprove = false,
            PersistMemory = false,
        });

        var agent = new FakeJobAgent("  finished  ");
        var factory = new FakeJobAgentFactory
        {
            PreparedAgent = new PreparedJobAgent(
                agent,
                new ToolSelectionSummary
                {
                    TotalAvailableTools = 5,
                    EligibleTools = 5,
                    SelectorUsed = false,
                })
        };
        var memoryStore = new FakeExecutionMemoryStore();
        var runner = new JobRunner(new FakeConfigLoader(config), factory, memoryStore);

        using var writer = new StringWriter();
        var exitCode = await runner.RunAsync("config.yaml", null, writer, CancellationToken.None);
        var output = writer.ToString();

        Assert.Equal(0, exitCode);
        Assert.Equal(0, memoryStore.LoadCallCount);
        Assert.Equal(0, memoryStore.SaveCallCount);
        Assert.Contains("> Running job: inspect", output);
        Assert.Contains("Selected tools: none", output);
        Assert.Contains("finished", output);
        Assert.DoesNotContain("  finished  ", output);
        Assert.Contains("Mutation tools available: no", agent.LastPrompt);
    }

    [Fact]
    public async Task RunAsync_PersistsTrimmedMemoryAndIncludesRecentHistory()
    {
        var config = CreateConfig(new JobDefinition
        {
            Name = "nightly",
            Prompt = new string('P', 4_100),
            Provider = "OpenAI",
            AutoApprove = true,
            PersistMemory = true,
            Schedule = "0 0 * * *",
            ThinkingLevel = "unexpected",
            AllowedTools = { "ReadTextFile" },
        });

        var existingDocument = new JobMemoryDocument
        {
            Entries = Enumerable.Range(0, 11)
                .Select(index => new JobMemoryEntry
                {
                    TimestampUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(index),
                    Prompt = $"Old prompt {index}",
                    Response = $"Old response {index}",
                })
                .ToList(),
        };

        var agent = new FakeJobAgent(new string('R', 8_100));
        var memoryStore = new FakeExecutionMemoryStore { LoadedDocument = existingDocument };
        var factory = new FakeJobAgentFactory
        {
            PreparedAgent = new PreparedJobAgent(
                agent,
                new ToolSelectionSummary
                {
                    TotalAvailableTools = 12,
                    EligibleTools = 1,
                    SelectorUsed = true,
                    AllowedTools = ["ReadTextFile"],
                    SelectedTools = ["ReadTextFile"],
                    Rationale = "Inspection is sufficient.",
                })
        };
        var runner = new JobRunner(new FakeConfigLoader(config), factory, memoryStore);

        using var workspace = new TestWorkspace();
        using var writer = new StringWriter();
        var configPath = workspace.WriteFile("nested/config.yaml", "ignored");

        var exitCode = await runner.RunAsync(configPath, null, writer, CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, memoryStore.LoadCallCount);
        Assert.Equal(1, memoryStore.SaveCallCount);
        Assert.Equal(Path.GetDirectoryName(Path.GetFullPath(configPath)), memoryStore.LastConfigDirectory);
        Assert.Equal("nightly", memoryStore.LastJobName);

        Assert.NotNull(memoryStore.SavedDocument);
        Assert.Equal(10, memoryStore.SavedDocument!.Entries.Count);

        var latestEntry = memoryStore.SavedDocument.Entries[^1];
        Assert.Equal(4_003, latestEntry.Prompt.Length);
        Assert.EndsWith("...", latestEntry.Prompt);
        Assert.Equal(8_003, latestEntry.Response.Length);
        Assert.EndsWith("...", latestEntry.Response);

        Assert.Contains("Requested reasoning level: low", agent.LastPrompt);
        Assert.Contains("Requested schedule metadata: 0 0 * * *", agent.LastPrompt);
        Assert.Contains("Previous run memory:", agent.LastPrompt);
        Assert.Contains("Old prompt 8", agent.LastPrompt);
        Assert.Contains("Old prompt 10", agent.LastPrompt);
        Assert.Contains("Task:", agent.LastPrompt);

        var output = writer.ToString();
        Assert.Contains("Tool allowlist: ReadTextFile", output);
        Assert.Contains("Selector used: yes", output);
        Assert.Contains("Selection rationale: Inspection is sufficient.", output);
    }

    [Fact]
    public async Task RunAsync_ContinuesAfterJobFailureAndReturnsOne()
    {
        var config = CreateConfig(
            new JobDefinition
            {
                Name = "first",
                Prompt = "fail",
                Provider = "OpenAI",
            },
            new JobDefinition
            {
                Name = "second",
                Prompt = "succeed",
                Provider = "OpenAI",
            });

        var factory = new FakeJobAgentFactory
        {
            Prepare = job => job.Name == "first"
                ? throw new InvalidOperationException("boom")
                : new PreparedJobAgent(new FakeJobAgent("done"), new ToolSelectionSummary())
        };
        var runner = new JobRunner(new FakeConfigLoader(config), factory, new FakeExecutionMemoryStore());

        using var writer = new StringWriter();
        var exitCode = await runner.RunAsync("config.yaml", null, writer, CancellationToken.None);
        var output = writer.ToString();

        Assert.Equal(1, exitCode);
        Assert.Contains("Job 'first' failed: boom", output);
        Assert.Contains("> Running job: second", output);
        Assert.Contains("done", output);
    }

    [Fact]
    public async Task RunAsync_HonorsCancellationBeforeExecution()
    {
        var config = CreateConfig(new JobDefinition
        {
            Name = "job",
            Prompt = "noop",
            Provider = "OpenAI",
        });

        var runner = new JobRunner(
            new FakeConfigLoader(config),
            new FakeJobAgentFactory { PreparedAgent = new PreparedJobAgent(new FakeJobAgent("done"), new ToolSelectionSummary()) },
            new FakeExecutionMemoryStore());

        using var writer = new StringWriter();
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => runner.RunAsync("config.yaml", null, writer, source.Token));
    }

    [Fact]
    public async Task ValidateAsync_WritesJobCount()
    {
        var config = CreateConfig(
            new JobDefinition { Name = "one", Prompt = "a", Provider = "OpenAI" },
            new JobDefinition { Name = "two", Prompt = "b", Provider = "OpenAI" });

        var runner = new JobRunner(new FakeConfigLoader(config), new FakeJobAgentFactory(), new FakeExecutionMemoryStore());
        using var writer = new StringWriter();

        var exitCode = await runner.ValidateAsync("config.yaml", writer, CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Contains("Configuration is valid. Jobs: 2.", writer.ToString());
    }

    [Fact]
    public async Task ListJobsAsync_OrdersJobsCaseInsensitively()
    {
        var config = CreateConfig(
            new JobDefinition { Name = "zeta", Prompt = "a", Provider = "OpenAI", Schedule = null, Enabled = true },
            new JobDefinition { Name = "Alpha", Prompt = "b", Provider = "OpenAI", Schedule = "daily", Enabled = false });

        var runner = new JobRunner(new FakeConfigLoader(config), new FakeJobAgentFactory(), new FakeExecutionMemoryStore());
        using var writer = new StringWriter();

        var exitCode = await runner.ListJobsAsync("config.yaml", writer, CancellationToken.None);
        var lines = writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(0, exitCode);
        Assert.StartsWith("- Alpha", lines[0]);
        Assert.Contains("Schedule=daily", lines[0]);
        Assert.StartsWith("- zeta", lines[1]);
        Assert.Contains("Schedule=manual", lines[1]);
    }

    private static AppConfig CreateConfig(params JobDefinition[] jobs)
    {
        var config = new AppConfig();
        config.OpenAI.ApiKey = "key";
        config.OpenAI.Model = "model";
        config.Jobs.AddRange(jobs);
        return config;
    }

    private sealed class FakeConfigLoader(AppConfig config) : IAppConfigLoader
    {
        public Task<AppConfig> LoadAsync(string path, CancellationToken cancellationToken)
        {
            return Task.FromResult(config);
        }
    }

    private sealed class FakeExecutionMemoryStore : IExecutionMemoryStore
    {
        public int LoadCallCount { get; private set; }

        public int SaveCallCount { get; private set; }

        public JobMemoryDocument LoadedDocument { get; init; } = new();

        public JobMemoryDocument? SavedDocument { get; private set; }

        public string? LastConfigDirectory { get; private set; }

        public string? LastJobName { get; private set; }

        public Task<JobMemoryDocument> LoadAsync(string configDirectory, string jobName, CancellationToken cancellationToken)
        {
            LoadCallCount++;
            LastConfigDirectory = configDirectory;
            LastJobName = jobName;
            return Task.FromResult(LoadedDocument);
        }

        public Task SaveAsync(string configDirectory, string jobName, JobMemoryDocument document, CancellationToken cancellationToken)
        {
            SaveCallCount++;
            LastConfigDirectory = configDirectory;
            LastJobName = jobName;
            SavedDocument = document;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeJobAgentFactory : IJobAgentFactory
    {
        public Func<JobDefinition, PreparedJobAgent>? Prepare { get; init; }

        public PreparedJobAgent? PreparedAgent { get; init; }

        public Task<PreparedJobAgent> CreateAsync(AppConfig config, JobDefinition job, string configDirectory, CancellationToken cancellationToken)
        {
            if (Prepare is not null)
            {
                return Task.FromResult(Prepare(job));
            }

            if (PreparedAgent is not null)
            {
                return Task.FromResult(PreparedAgent);
            }

            throw new InvalidOperationException("No prepared agent was configured.");
        }
    }

    private sealed class FakeJobAgent(string response) : IJobAgent
    {
        public string LastPrompt { get; private set; } = string.Empty;

        public Task<string> RunAsync(string prompt, CancellationToken cancellationToken)
        {
            LastPrompt = prompt;
            return Task.FromResult(response);
        }
    }
}