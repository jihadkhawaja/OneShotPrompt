using System.Text;
using OneShotPrompt.Application.Abstractions;
using OneShotPrompt.Core.Enums;
using OneShotPrompt.Core.Models;

namespace OneShotPrompt.Application.Services;

public sealed class JobRunner(
    IAppConfigLoader configLoader,
    IJobAgentFactory agentFactory,
    IExecutionMemoryStore memoryStore,
    IJobEventSink? eventSink = null)
{
    public async Task<int> RunAsync(string configPath, string? jobName, TextWriter output, CancellationToken cancellationToken)
    {
        var config = await configLoader.LoadAsync(
            configPath,
            cancellationToken,
            CreateRunLoadOptions(jobName));
        var jobs = SelectJobs(config, jobName);

        if (jobs.Count == 0)
        {
            await output.WriteLineAsync(jobName is null
                ? "No enabled jobs were found in the configuration."
                : $"No enabled job named '{jobName}' was found.");
            return 1;
        }

        var configDirectory = Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? Environment.CurrentDirectory;
        var hasFailures = false;

        foreach (var job in jobs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await output.WriteLineAsync($"> Running job: {job.Name}");
            eventSink?.Emit(new JobLogEvent($"Job started: {job.Name}"));

            var persistMemory = job.ResolvePersistMemory(config);
            var memory = persistMemory
                ? await memoryStore.LoadAsync(configDirectory, job.Name, cancellationToken)
                : new JobMemoryDocument();

            var prompt = BuildPrompt(config, job, memory);

            try
            {
                await using var preparedAgent = await agentFactory.CreateAsync(config, job, configDirectory, cancellationToken);
                await WriteToolSelectionSummaryAsync(output, preparedAgent.ToolSelection);

                var response = await preparedAgent.Agent.RunAsync(prompt, cancellationToken);

                eventSink?.Emit(new OutputBoundaryEvent());
                await output.WriteLineAsync(response.Trim());
                await output.WriteLineAsync(string.Empty);
                eventSink?.Emit(new JobLogEvent($"Job completed: {job.Name}"));

                if (persistMemory)
                {
                    memory.Entries.Add(new JobMemoryEntry
                    {
                        TimestampUtc = DateTimeOffset.UtcNow,
                        Prompt = Truncate(job.Prompt, 4_000),
                        Response = Truncate(response, 8_000),
                    });

                    memory.Entries = memory.Entries
                        .OrderByDescending(entry => entry.TimestampUtc)
                        .Take(10)
                        .OrderBy(entry => entry.TimestampUtc)
                        .ToList();

                    await memoryStore.SaveAsync(configDirectory, job.Name, memory, cancellationToken);
                }
            }
            catch (Exception exception)
            {
                hasFailures = true;
                eventSink?.Emit(new OutputBoundaryEvent());
                await output.WriteLineAsync($"Job '{job.Name}' failed: {exception.Message}");
                await output.WriteLineAsync(string.Empty);
                eventSink?.Emit(new JobLogEvent($"Job failed: {job.Name} -- {exception.Message}"));
            }
        }

        return hasFailures ? 1 : 0;
    }

    public async Task<int> ListenAsync(
        string configPath,
        string jobName,
        Func<CancellationToken, Task<JobTriggerSignal>> waitForTriggerAsync,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(jobName))
        {
            throw new ArgumentException("A job name is required for listen mode.", nameof(jobName));
        }

        ArgumentNullException.ThrowIfNull(waitForTriggerAsync);

        var config = await configLoader.LoadAsync(
            configPath,
            cancellationToken,
            CreateRunLoadOptions(jobName));
        var jobs = SelectJobs(config, jobName);

        if (jobs.Count == 0)
        {
            await output.WriteLineAsync($"No enabled job named '{jobName}' was found.");
            return 1;
        }

        await output.WriteLineAsync($"> Listening for triggers for job: {jobName}");
        await output.WriteLineAsync("  Source: whatsapp-personal-channel");
        await output.WriteLineAsync("  Press Ctrl+C to stop.");
        await output.WriteLineAsync(string.Empty);
        eventSink?.Emit(new JobLogEvent($"Listener started: {jobName}"));

        var hasFailures = false;
        var triggerCount = 0;

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var trigger = await waitForTriggerAsync(cancellationToken);
                triggerCount++;

                eventSink?.Emit(new OutputBoundaryEvent());
                eventSink?.Emit(new JobLogEvent($"Trigger received: {jobName} -- {trigger.Source} -- {trigger.Summary}"));
                await output.WriteLineAsync($"> Trigger received: {trigger.Source} | {trigger.Summary}");

                var exitCode = await RunAsync(configPath, jobName, output, cancellationToken);
                if (exitCode != 0)
                {
                    hasFailures = true;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await output.WriteLineAsync($"> Listener stopped for job: {jobName} (triggers handled: {triggerCount})");
            await output.WriteLineAsync(string.Empty);
            eventSink?.Emit(new JobLogEvent($"Listener stopped: {jobName} -- triggers handled: {triggerCount}"));
            return hasFailures ? 1 : 0;
        }
        catch (Exception exception)
        {
            eventSink?.Emit(new OutputBoundaryEvent());
            await output.WriteLineAsync($"Listener for job '{jobName}' failed: {exception.Message}");
            await output.WriteLineAsync(string.Empty);
            eventSink?.Emit(new JobLogEvent($"Listener failed: {jobName} -- {exception.Message}"));
            return 1;
        }
    }

    public async Task<int> RunAdHocAsync(AppConfig config, JobDefinition job, string configDirectory, TextWriter output, CancellationToken cancellationToken)
    {
        await output.WriteLineAsync($"> Running ad-hoc prompt ({job.Provider})");
        eventSink?.Emit(new JobLogEvent("Ad-hoc prompt started"));

        var prompt = BuildAdHocPrompt(config, job);

        try
        {
            await using var preparedAgent = await agentFactory.CreateAsync(config, job, configDirectory, cancellationToken);
            await WriteToolSelectionSummaryAsync(output, preparedAgent.ToolSelection);

            var response = await preparedAgent.Agent.RunAsync(prompt, cancellationToken);

            eventSink?.Emit(new OutputBoundaryEvent());
            await output.WriteLineAsync(response.Trim());
            await output.WriteLineAsync(string.Empty);
            eventSink?.Emit(new JobLogEvent("Ad-hoc prompt completed"));
            return 0;
        }
        catch (Exception exception)
        {
            eventSink?.Emit(new OutputBoundaryEvent());
            await output.WriteLineAsync($"Ad-hoc prompt failed: {exception.Message}");
            await output.WriteLineAsync(string.Empty);
            eventSink?.Emit(new JobLogEvent($"Ad-hoc prompt failed: {exception.Message}"));
            return 1;
        }
    }

    public async Task<int> ValidateAsync(string configPath, TextWriter output, CancellationToken cancellationToken)
    {
        var config = await configLoader.LoadAsync(
            configPath,
            cancellationToken,
            new ConfigLoadOptions
            {
                ProviderValidationScope = ProviderValidationScope.AllJobs,
            });
        await output.WriteLineAsync($"Configuration is valid. Jobs: {config.Jobs.Count}.");
        return 0;
    }

    public async Task<int> ListJobsAsync(string configPath, TextWriter output, CancellationToken cancellationToken)
    {
        var config = await configLoader.LoadAsync(
            configPath,
            cancellationToken,
            new ConfigLoadOptions
            {
                ProviderValidationScope = ProviderValidationScope.None,
            });

        foreach (var job in config.Jobs.OrderBy(job => job.Name, StringComparer.OrdinalIgnoreCase))
        {
            await output.WriteLineAsync($"- {job.Name} | Provider={job.Provider} | Enabled={job.Enabled} | Schedule={job.Schedule ?? "manual"}");
        }

        return 0;
    }

    private static ConfigLoadOptions CreateRunLoadOptions(string? jobName)
    {
        return new ConfigLoadOptions
        {
            ProviderValidationScope = string.IsNullOrWhiteSpace(jobName)
                ? ProviderValidationScope.EnabledJobs
                : ProviderValidationScope.SelectedJobs,
            SelectedJobNames = string.IsNullOrWhiteSpace(jobName) ? [] : [jobName],
        };
    }

    private static List<JobDefinition> SelectJobs(AppConfig config, string? jobName)
    {
        var query = config.Jobs.Where(job => job.Enabled);

        if (!string.IsNullOrWhiteSpace(jobName))
        {
            query = query.Where(job => string.Equals(job.Name, jobName, StringComparison.OrdinalIgnoreCase));
        }

        return query.ToList();
    }

    private static string BuildPrompt(AppConfig config, JobDefinition job, JobMemoryDocument memory)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Job: {job.Name}");
        builder.AppendLine($"Provider: {job.Provider}");
        builder.AppendLine($"Workflow: {job.ResolveWorkflow()}");
        builder.AppendLine($"Requested reasoning level: {NormalizeThinkingLevel(job.ResolveThinkingLevel(config))}");
        builder.AppendLine($"Mutation tools available: {(job.AutoApprove ? "yes" : "no")}");
        builder.AppendLine("Tool usage policy: shortlist the minimum relevant tools first, then use only that shortlist unless blocked.");

        if (!string.IsNullOrWhiteSpace(job.Schedule))
        {
            builder.AppendLine($"Requested schedule metadata: {job.Schedule}");
        }

        if (memory.Entries.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Previous run memory:");

            foreach (var entry in memory.Entries.OrderByDescending(entry => entry.TimestampUtc).Take(3).Reverse())
            {
                builder.AppendLine($"- {entry.TimestampUtc:O}");
                builder.AppendLine($"  Prompt: {Truncate(entry.Prompt, 250)}");
                builder.AppendLine($"  Response: {Truncate(entry.Response, 350)}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Task:");
        builder.AppendLine(job.Prompt.Trim());

        return builder.ToString().Trim();
    }

    private static string BuildAdHocPrompt(AppConfig config, JobDefinition job)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Provider: {job.Provider}");
        builder.AppendLine($"Workflow: {job.ResolveWorkflow()}");
        builder.AppendLine($"Requested reasoning level: {NormalizeThinkingLevel(job.ResolveThinkingLevel(config))}");
        builder.AppendLine($"Mutation tools available: {(job.AutoApprove ? "yes" : "no")}");
        builder.AppendLine("Tool usage policy: shortlist the minimum relevant tools first, then use only that shortlist unless blocked.");
        builder.AppendLine();
        builder.AppendLine("Task:");
        builder.AppendLine(job.Prompt.Trim());
        return builder.ToString().Trim();
    }

    private static string NormalizeThinkingLevel(string value)
    {
        return Enum.TryParse<ThinkingLevel>(value, ignoreCase: true, out var level)
            ? level.ToString().ToLowerInvariant()
            : "low";
    }

    private static async Task WriteToolSelectionSummaryAsync(TextWriter output, ToolSelectionSummary summary)
    {
        await output.WriteLineAsync($"  Tools available before allowlist: {summary.TotalAvailableTools}");
        await output.WriteLineAsync($"  Tools eligible for selection: {summary.EligibleTools}");
        await output.WriteLineAsync($"  Workflow: {summary.Workflow}");

        if (summary.AllowedTools.Count > 0)
        {
            await output.WriteLineAsync($"  Tool allowlist: {string.Join(", ", summary.AllowedTools)}");
        }

        await output.WriteLineAsync($"  Selector used: {(summary.SelectorUsed ? "yes" : "no")}");
        await output.WriteLineAsync($"  Selected tools: {(summary.SelectedTools.Count == 0 ? "none" : string.Join(", ", summary.SelectedTools))}");

        if (!string.IsNullOrWhiteSpace(summary.Rationale))
        {
            await output.WriteLineAsync($"  Selection rationale: {summary.Rationale}");
        }

        foreach (var generatedAgent in summary.GeneratedAgents)
        {
            var assignedTools = generatedAgent.AssignedTools.Count == 0
                ? "none"
                : string.Join(", ", generatedAgent.AssignedTools);
            await output.WriteLineAsync($"  Generated agent: {generatedAgent.Name} | Tools={assignedTools}");
            await output.WriteLineAsync($"    {generatedAgent.Description}");
        }

        await output.WriteLineAsync(string.Empty);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength] + "...";
    }
}