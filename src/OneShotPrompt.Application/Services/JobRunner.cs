using System.Text;
using OneShotPrompt.Application.Abstractions;
using OneShotPrompt.Core.Enums;
using OneShotPrompt.Core.Models;

namespace OneShotPrompt.Application.Services;

public sealed class JobRunner(
    IAppConfigLoader configLoader,
    IJobAgentFactory agentFactory,
    IExecutionMemoryStore memoryStore)
{
    public async Task<int> RunAsync(string configPath, string? jobName, TextWriter output, CancellationToken cancellationToken)
    {
        var config = await configLoader.LoadAsync(configPath, cancellationToken);
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

            var persistMemory = job.ResolvePersistMemory(config);
            var memory = persistMemory
                ? await memoryStore.LoadAsync(configDirectory, job.Name, cancellationToken)
                : new JobMemoryDocument();

            var prompt = BuildPrompt(config, job, memory);

            try
            {
                var agent = await agentFactory.CreateAsync(config, job, configDirectory, cancellationToken);
                var response = await agent.RunAsync(prompt, cancellationToken);

                await output.WriteLineAsync(response.Trim());
                await output.WriteLineAsync(string.Empty);

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
                await output.WriteLineAsync($"Job '{job.Name}' failed: {exception.Message}");
                await output.WriteLineAsync(string.Empty);
            }
        }

        return hasFailures ? 1 : 0;
    }

    public async Task<int> ValidateAsync(string configPath, TextWriter output, CancellationToken cancellationToken)
    {
        var config = await configLoader.LoadAsync(configPath, cancellationToken);
        await output.WriteLineAsync($"Configuration is valid. Jobs: {config.Jobs.Count}.");
        return 0;
    }

    public async Task<int> ListJobsAsync(string configPath, TextWriter output, CancellationToken cancellationToken)
    {
        var config = await configLoader.LoadAsync(configPath, cancellationToken);

        foreach (var job in config.Jobs.OrderBy(job => job.Name, StringComparer.OrdinalIgnoreCase))
        {
            await output.WriteLineAsync($"- {job.Name} | Provider={job.Provider} | Enabled={job.Enabled} | Schedule={job.Schedule ?? "manual"}");
        }

        return 0;
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
        builder.AppendLine($"Requested reasoning level: {NormalizeThinkingLevel(job.ResolveThinkingLevel(config))}");
        builder.AppendLine($"Mutation tools available: {(job.AutoApprove ? "yes" : "no")}");

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

    private static string NormalizeThinkingLevel(string value)
    {
        return Enum.TryParse<ThinkingLevel>(value, ignoreCase: true, out var level)
            ? level.ToString().ToLowerInvariant()
            : "low";
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