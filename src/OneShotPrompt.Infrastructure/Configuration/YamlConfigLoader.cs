using OneShotPrompt.Application.Abstractions;
using OneShotPrompt.Core.Enums;
using OneShotPrompt.Core.Models;

namespace OneShotPrompt.Infrastructure.Configuration;

public sealed class YamlConfigLoader : IAppConfigLoader
{
    public async Task<AppConfig> LoadAsync(string path, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Configuration file was not found: {fullPath}", fullPath);
        }

        var lines = await File.ReadAllLinesAsync(fullPath, cancellationToken);
        var config = Parse(lines);
        Validate(config, fullPath);
        return config;
    }

    private static AppConfig Parse(IReadOnlyList<string> lines)
    {
        var config = new AppConfig();

        for (var index = 0; index < lines.Count;)
        {
            if (SkipLine(lines[index]))
            {
                index++;
                continue;
            }

            EnsureIndent(lines[index], 0, "root entry");
            var trimmed = lines[index].Trim();

            if (trimmed.EndsWith(':'))
            {
                var key = trimmed[..^1].Trim();
                index++;

                switch (key)
                {
                    case "OpenAI":
                        ParseProviderSection(lines, ref index, 2, AssignOpenAI);
                        break;
                    case "Anthropic":
                        ParseProviderSection(lines, ref index, 2, AssignAnthropic);
                        break;
                    case "OpenAICompatible":
                        ParseProviderSection(lines, ref index, 2, AssignOpenAICompatible);
                        break;
                    case "GitHubCopilot":
                        ParseProviderSection(lines, ref index, 2, AssignGitHubCopilot);
                        break;
                    case "Jobs":
                        ParseJobs(lines, ref index, config.Jobs);
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported YAML section '{key}'.");
                }

                continue;
            }

            var (rootKey, rootValue) = ParseKeyValue(trimmed);
            AssignRootScalar(config, rootKey, rootValue);
            index++;
        }

        return config;

        void AssignOpenAI(string key, object value)
        {
            if (key.Equals("ApiKey", StringComparison.OrdinalIgnoreCase))
            {
                config.OpenAI.ApiKey = ToStringValue(value);
                return;
            }

            if (key.Equals("Model", StringComparison.OrdinalIgnoreCase))
            {
                config.OpenAI.Model = ToStringValue(value);
                return;
            }

            throw new InvalidOperationException($"Unsupported OpenAI setting '{key}'.");
        }

        void AssignAnthropic(string key, object value)
        {
            if (key.Equals("ApiKey", StringComparison.OrdinalIgnoreCase))
            {
                config.Anthropic.ApiKey = ToStringValue(value);
                return;
            }

            if (key.Equals("Model", StringComparison.OrdinalIgnoreCase))
            {
                config.Anthropic.Model = ToStringValue(value);
                return;
            }

            throw new InvalidOperationException($"Unsupported Anthropic setting '{key}'.");
        }

        void AssignOpenAICompatible(string key, object value)
        {
            if (key.Equals("Endpoint", StringComparison.OrdinalIgnoreCase))
            {
                config.OpenAICompatible.Endpoint = ToStringValue(value);
                return;
            }

            if (key.Equals("ApiKey", StringComparison.OrdinalIgnoreCase))
            {
                config.OpenAICompatible.ApiKey = ToStringValue(value);
                return;
            }

            if (key.Equals("Model", StringComparison.OrdinalIgnoreCase))
            {
                config.OpenAICompatible.Model = ToStringValue(value);
                return;
            }

            throw new InvalidOperationException($"Unsupported OpenAICompatible setting '{key}'.");
        }

        void AssignGitHubCopilot(string key, object value)
        {
            if (key.Equals("Model", StringComparison.OrdinalIgnoreCase))
            {
                config.GitHubCopilot.Model = ToStringValue(value);
                return;
            }

            if (key.Equals("CliPath", StringComparison.OrdinalIgnoreCase))
            {
                config.GitHubCopilot.CliPath = ToStringValue(value);
                return;
            }

            if (key.Equals("CliUrl", StringComparison.OrdinalIgnoreCase))
            {
                config.GitHubCopilot.CliUrl = ToStringValue(value);
                return;
            }

            if (key.Equals("LogLevel", StringComparison.OrdinalIgnoreCase))
            {
                config.GitHubCopilot.LogLevel = ToStringValue(value);
                return;
            }

            if (key.Equals("GitHubToken", StringComparison.OrdinalIgnoreCase))
            {
                config.GitHubCopilot.GitHubToken = ToStringValue(value);
                return;
            }

            if (key.Equals("UseLoggedInUser", StringComparison.OrdinalIgnoreCase))
            {
                config.GitHubCopilot.UseLoggedInUser = ToBoolValue(value, key);
                return;
            }

            if (key.Equals("AutoStart", StringComparison.OrdinalIgnoreCase))
            {
                config.GitHubCopilot.AutoStart = ToBoolValue(value, key);
                return;
            }

            if (key.Equals("AutoRestart", StringComparison.OrdinalIgnoreCase))
            {
                config.GitHubCopilot.AutoRestart = ToBoolValue(value, key);
                return;
            }

            throw new InvalidOperationException($"Unsupported GitHubCopilot setting '{key}'.");
        }
    }

    private static void ParseProviderSection(IReadOnlyList<string> lines, ref int index, int indent, Action<string, object> assign)
    {
        while (index < lines.Count)
        {
            if (SkipLine(lines[index]))
            {
                index++;
                continue;
            }

            var currentIndent = CountIndent(lines[index]);

            if (currentIndent < indent)
            {
                break;
            }

            EnsureIndent(lines[index], indent, "provider setting");
            var (key, value) = ParseKeyValue(lines[index].Trim());
            assign(key, value);
            index++;
        }
    }

    private static void ParseJobs(IReadOnlyList<string> lines, ref int index, ICollection<JobDefinition> jobs)
    {
        while (index < lines.Count)
        {
            if (SkipLine(lines[index]))
            {
                index++;
                continue;
            }

            var currentIndent = CountIndent(lines[index]);
            if (currentIndent < 2)
            {
                break;
            }

            EnsureIndent(lines[index], 2, "job entry");
            var trimmed = lines[index].Trim();

            if (!trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Job entries must start with '- '.");
            }

            var job = new JobDefinition();
            var firstEntry = trimmed[2..].Trim();
            if (!string.IsNullOrWhiteSpace(firstEntry))
            {
                var (key, value) = ParseKeyValue(firstEntry);
                AssignJobScalar(job, key, value);
            }

            index++;

            while (index < lines.Count)
            {
                if (SkipLine(lines[index]))
                {
                    index++;
                    continue;
                }

                currentIndent = CountIndent(lines[index]);
                if (currentIndent < 4)
                {
                    break;
                }

                EnsureIndent(lines[index], 4, "job property");
                var (key, value) = ParseKeyValue(lines[index].Trim());
                AssignJobScalar(job, key, value);
                index++;
            }

            jobs.Add(job);
        }
    }

    private static void AssignRootScalar(AppConfig config, string key, object value)
    {
        if (key.Equals("ThinkingLevel", StringComparison.OrdinalIgnoreCase))
        {
            config.ThinkingLevel = ToStringValue(value);
            return;
        }

        if (key.Equals("PersistMemory", StringComparison.OrdinalIgnoreCase))
        {
            config.PersistMemory = ToBoolValue(value, key);
            return;
        }

        throw new InvalidOperationException($"Unsupported root setting '{key}'.");
    }

    private static void AssignJobScalar(JobDefinition job, string key, object value)
    {
        if (key.Equals("Name", StringComparison.OrdinalIgnoreCase))
        {
            job.Name = ToStringValue(value);
            return;
        }

        if (key.Equals("Prompt", StringComparison.OrdinalIgnoreCase))
        {
            job.Prompt = ToStringValue(value);
            return;
        }

        if (key.Equals("Provider", StringComparison.OrdinalIgnoreCase))
        {
            job.Provider = ToStringValue(value);
            return;
        }

        if (key.Equals("AutoApprove", StringComparison.OrdinalIgnoreCase))
        {
            job.AutoApprove = ToBoolValue(value, key);
            return;
        }

        if (key.Equals("PersistMemory", StringComparison.OrdinalIgnoreCase))
        {
            job.PersistMemory = ToBoolValue(value, key);
            return;
        }

        if (key.Equals("ThinkingLevel", StringComparison.OrdinalIgnoreCase))
        {
            job.ThinkingLevel = ToStringValue(value);
            return;
        }

        if (key.Equals("Schedule", StringComparison.OrdinalIgnoreCase))
        {
            job.Schedule = ToStringValue(value);
            return;
        }

        if (key.Equals("Enabled", StringComparison.OrdinalIgnoreCase))
        {
            job.Enabled = ToBoolValue(value, key);
            return;
        }

        if (key.Equals("AllowedTools", StringComparison.OrdinalIgnoreCase))
        {
            job.AllowedTools.Clear();

            foreach (var toolName in ParseAllowedTools(value))
            {
                job.AllowedTools.Add(toolName);
            }

            return;
        }

        throw new InvalidOperationException($"Unsupported job setting '{key}'.");
    }

    private static void Validate(AppConfig config, string fullPath)
    {
        if (config.Jobs.Count == 0)
        {
            throw new InvalidOperationException($"Configuration '{fullPath}' must define at least one job.");
        }

        EnsureThinkingLevel(config.ThinkingLevel, "ThinkingLevel");
        var duplicateName = config.Jobs
            .GroupBy(job => job.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1);

        if (duplicateName is not null)
        {
            throw new InvalidOperationException($"Job name '{duplicateName.Key}' is defined more than once.");
        }

        foreach (var job in config.Jobs)
        {
            if (string.IsNullOrWhiteSpace(job.Name))
            {
                throw new InvalidOperationException("Each job must define Name.");
            }

            if (string.IsNullOrWhiteSpace(job.Prompt))
            {
                throw new InvalidOperationException($"Job '{job.Name}' must define Prompt.");
            }

            if (!Enum.TryParse<JobProvider>(job.Provider, ignoreCase: true, out var provider))
            {
                throw new InvalidOperationException($"Job '{job.Name}' uses unsupported provider '{job.Provider}'.");
            }

            if (!string.IsNullOrWhiteSpace(job.ThinkingLevel))
            {
                EnsureThinkingLevel(job.ThinkingLevel, $"Jobs[{job.Name}].ThinkingLevel");
            }

            ValidateAllowedTools(job);

            ValidateProviderSettings(config, job.Name, provider);
        }
    }

    private static void ValidateAllowedTools(JobDefinition job)
    {
        var duplicateTool = job.AllowedTools
            .GroupBy(tool => tool, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateTool is not null)
        {
            throw new InvalidOperationException($"Job '{job.Name}' defines duplicate AllowedTools entry '{duplicateTool.Key}'.");
        }

        foreach (var toolName in job.AllowedTools)
        {
            if (!BuiltInToolCatalog.IsKnown(toolName))
            {
                throw new InvalidOperationException($"Job '{job.Name}' uses unknown AllowedTools entry '{toolName}'.");
            }

            if (!job.AutoApprove && BuiltInToolCatalog.RequiresMutation(toolName))
            {
                throw new InvalidOperationException($"Job '{job.Name}' cannot allow mutation tool '{toolName}' when AutoApprove is false.");
            }
        }
    }

    private static void ValidateProviderSettings(AppConfig config, string jobName, JobProvider provider)
    {
        switch (provider)
        {
            case JobProvider.OpenAI:
                EnsureRequired(config.OpenAI.ApiKey, jobName, "OpenAI.ApiKey");
                EnsureRequired(config.OpenAI.Model, jobName, "OpenAI.Model");
                break;
            case JobProvider.Anthropic:
                EnsureRequired(config.Anthropic.ApiKey, jobName, "Anthropic.ApiKey");
                EnsureRequired(config.Anthropic.Model, jobName, "Anthropic.Model");
                break;
            case JobProvider.OpenAICompatible:
                EnsureRequired(config.OpenAICompatible.Endpoint, jobName, "OpenAICompatible.Endpoint");
                EnsureRequired(config.OpenAICompatible.ApiKey, jobName, "OpenAICompatible.ApiKey");
                EnsureRequired(config.OpenAICompatible.Model, jobName, "OpenAICompatible.Model");
                break;
            case JobProvider.GitHubCopilot:
                ValidateGitHubCopilotSettings(config.GitHubCopilot, jobName);
                break;
            default:
                throw new InvalidOperationException($"Job '{jobName}' uses unsupported provider '{provider}'.");
        }
    }

    private static void ValidateGitHubCopilotSettings(GitHubCopilotProviderSettings settings, string jobName)
    {
        if (!string.IsNullOrWhiteSpace(settings.CliUrl) && !string.IsNullOrWhiteSpace(settings.CliPath))
        {
            throw new InvalidOperationException($"Job '{jobName}' cannot set both 'GitHubCopilot.CliUrl' and 'GitHubCopilot.CliPath'.");
        }

        if (!string.IsNullOrWhiteSpace(settings.CliUrl) &&
            (!string.IsNullOrWhiteSpace(settings.GitHubToken) || settings.UseLoggedInUser.HasValue))
        {
            throw new InvalidOperationException($"Job '{jobName}' cannot combine 'GitHubCopilot.CliUrl' with 'GitHubCopilot.GitHubToken' or 'GitHubCopilot.UseLoggedInUser'.");
        }
    }

    private static void EnsureRequired(string value, string jobName, string setting)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Job '{jobName}' requires '{setting}' to be configured.");
        }
    }

    private static void EnsureThinkingLevel(string value, string location)
    {
        if (!Enum.TryParse<ThinkingLevel>(value, ignoreCase: true, out _))
        {
            throw new InvalidOperationException($"'{location}' must be one of: low, medium, high.");
        }
    }

    private static List<string> ParseAllowedTools(object value)
    {
        var raw = ToStringValue(value).Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        if (raw.StartsWith("[", StringComparison.Ordinal) && raw.EndsWith("]", StringComparison.Ordinal) && raw.Length >= 2)
        {
            raw = raw[1..^1];
        }

        return raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(tool => tool.Trim().Trim('"', '\''))
            .Where(tool => !string.IsNullOrWhiteSpace(tool))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static (string Key, object Value) ParseKeyValue(string line)
    {
        var separatorIndex = line.IndexOf(':');
        if (separatorIndex <= 0)
        {
            throw new InvalidOperationException($"Invalid YAML entry '{line}'. Expected 'key: value'.");
        }

        var key = line[..separatorIndex].Trim();
        var valueText = line[(separatorIndex + 1)..].Trim();
        return (key, ParseScalar(valueText));
    }

    private static object ParseScalar(string value)
    {
        if (value.Length == 0)
        {
            return string.Empty;
        }

        if ((value.StartsWith('"') && value.EndsWith('"')) || (value.StartsWith('\'') && value.EndsWith('\'')))
        {
            var inner = value[1..^1];
            return inner
                .Replace("\\\"", "\"")
                .Replace("\\'", "'")
                .Replace("\\\\", "\\");
        }

        if (bool.TryParse(value, out var boolValue))
        {
            return boolValue;
        }

        return value;
    }

    private static string ToStringValue(object value) => value.ToString() ?? string.Empty;

    private static bool ToBoolValue(object value, string key)
    {
        return value switch
        {
            bool boolValue => boolValue,
            _ => throw new InvalidOperationException($"'{key}' must be a boolean value."),
        };
    }

    private static bool SkipLine(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length == 0 || trimmed.StartsWith('#');
    }

    private static int CountIndent(string line)
    {
        var count = 0;

        while (count < line.Length && line[count] == ' ')
        {
            count++;
        }

        return count;
    }

    private static void EnsureIndent(string line, int expectedIndent, string scope)
    {
        var actualIndent = CountIndent(line);
        if (actualIndent != expectedIndent)
        {
            throw new InvalidOperationException($"Invalid indentation for {scope}. Expected {expectedIndent} spaces but found {actualIndent}.");
        }
    }
}