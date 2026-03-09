using OneShotPrompt.Core.Enums;

namespace OneShotPrompt.Core.Models;

public sealed class AppConfig
{
    public OpenAIProviderSettings OpenAI { get; } = new();

    public AnthropicProviderSettings Anthropic { get; } = new();

    public GeminiProviderSettings Gemini { get; } = new();

    public OpenAICompatibleProviderSettings OpenAICompatible { get; } = new();

    public GitHubCopilotProviderSettings GitHubCopilot { get; } = new();

    public string ThinkingLevel { get; set; } = "low";

    public bool PersistMemory { get; set; } = true;

    public List<JobDefinition> Jobs { get; } = [];
}

public sealed class OpenAIProviderSettings
{
    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "gpt-5-nano";
}

public sealed class AnthropicProviderSettings
{
    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "claude-haiku-4-5";
}

public sealed class GeminiProviderSettings
{
    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "gemini-2.5-flash";
}

public sealed class OpenAICompatibleProviderSettings
{
    public string Endpoint { get; set; } = "http://localhost:1234/v1";

    public string ApiKey { get; set; } = "lm-studio";

    public string Model { get; set; } = "default";
}

public sealed class GitHubCopilotProviderSettings
{
    public string Model { get; set; } = "gpt-5";

    public string CliPath { get; set; } = string.Empty;

    public string CliUrl { get; set; } = string.Empty;

    public string LogLevel { get; set; } = "info";

    public string GitHubToken { get; set; } = string.Empty;

    public bool? UseLoggedInUser { get; set; }

    public bool AutoStart { get; set; } = true;

    public bool AutoRestart { get; set; } = true;
}

public sealed class JobDefinition
{
    public string Name { get; set; } = string.Empty;

    public string Prompt { get; set; } = string.Empty;

    public string Provider { get; set; } = nameof(JobProvider.OpenAI);

    public bool AutoApprove { get; set; }

    public bool? PersistMemory { get; set; }

    public string? ThinkingLevel { get; set; }

    public string? Schedule { get; set; }

    public bool Enabled { get; set; } = true;

    public List<string> AllowedTools { get; } = [];

    public bool ResolvePersistMemory(AppConfig config) => PersistMemory ?? config.PersistMemory;

    public string ResolveThinkingLevel(AppConfig config) => ThinkingLevel ?? config.ThinkingLevel;
}

public sealed class JobMemoryDocument
{
    public List<JobMemoryEntry> Entries { get; set; } = [];
}

public sealed class JobMemoryEntry
{
    public DateTimeOffset TimestampUtc { get; set; }

    public string Prompt { get; set; } = string.Empty;

    public string Response { get; set; } = string.Empty;
}