using Anthropic;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OneShotPrompt.Application.Abstractions;
using OneShotPrompt.Core.Enums;
using OneShotPrompt.Core.Models;
using OneShotPrompt.Infrastructure.Tools;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.Text;

namespace OneShotPrompt.Infrastructure.Providers;

public sealed class AgentFactory(IJobEventSink? eventSink = null) : IJobAgentFactory
{
    public async Task<PreparedJobAgent> CreateAsync(AppConfig config, JobDefinition job, string configDirectory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Enum.TryParse<JobProvider>(job.Provider, ignoreCase: true, out var provider))
        {
            throw new InvalidOperationException($"Unsupported provider '{job.Provider}'.");
        }

        var availableTools = BuildToolDefinitions(job, configDirectory);
        var eligibleTools = ApplyAllowlist(job, availableTools);
        var chatClient = CreateChatClient(config, provider);
        var selection = await SelectToolsAsync(chatClient, config, job, configDirectory, eligibleTools, cancellationToken);
        var instructions = BuildExecutionInstructions(config, job, availableTools.Count, selection.SelectedTools, job.AllowedTools);

        IChatClient executionClient = eventSink is not null
            ? new ObservableChatClient(chatClient, eventSink)
            : chatClient;

        var agent = CreateAgent(
            executionClient,
            job.Name,
            selection.SelectedTools.Select(tool => tool.CreateTool()).ToList(),
            instructions,
            configDirectory);

        return new PreparedJobAgent(
            new AgentFrameworkJobAgent(agent),
            new ToolSelectionSummary
            {
                TotalAvailableTools = availableTools.Count,
                EligibleTools = eligibleTools.Count,
                SelectorUsed = selection.SelectorUsed,
                AllowedTools = [.. job.AllowedTools],
                SelectedTools = [.. selection.SelectedTools.Select(tool => tool.Name)],
                Rationale = selection.Rationale,
            });
    }

    private static IChatClient CreateChatClient(AppConfig config, JobProvider provider)
    {
        return provider switch
        {
            JobProvider.OpenAI => new OpenAIClient(config.OpenAI.ApiKey)
                .GetChatClient(config.OpenAI.Model)
                .AsIChatClient(),
            JobProvider.OpenAICompatible => new ChatClient(
                model: config.OpenAICompatible.Model,
                credential: new ApiKeyCredential(config.OpenAICompatible.ApiKey),
                options: new OpenAIClientOptions
                {
                    Endpoint = new Uri(config.OpenAICompatible.Endpoint),
                }).AsIChatClient(),
            JobProvider.Anthropic => new AnthropicClient
            {
                ApiKey = config.Anthropic.ApiKey,
            }.AsIChatClient(config.Anthropic.Model),
            _ => throw new InvalidOperationException($"Unsupported provider '{provider}'."),
        };
    }

    private static AIAgent CreateAgent(IChatClient chatClient, string agentName, IList<AITool> tools, string instructions, string configDirectory)
    {
        var options = new ChatClientAgentOptions
        {
            Name = agentName,
            ChatOptions = new ChatOptions
            {
                Instructions = instructions,
                Tools = tools,
            },
            AIContextProviders = [CreateSkillsProvider(configDirectory)],
        };

        return chatClient.AsAIAgent(options);
    }

    private static async Task<ToolSelectionDecision> SelectToolsAsync(
        IChatClient chatClient,
        AppConfig config,
        JobDefinition job,
        string configDirectory,
        IReadOnlyList<ToolDefinition> availableTools,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (availableTools.Count == 0)
        {
            return new ToolSelectionDecision([], false, "No eligible tools remained after allowlist filtering.");
        }

        if (availableTools.Count == 1)
        {
            return new ToolSelectionDecision(availableTools, false, "Selector skipped because exactly one eligible tool was available.");
        }

        var selector = CreateAgent(
            chatClient,
            $"{job.Name}-tool-selector",
            [],
            BuildToolSelectionInstructions(config, job, availableTools.Count),
            configDirectory);

        var response = await selector.RunAsync(BuildToolSelectionPrompt(config, job, availableTools));
        var parseResult = ParseToolSelectionResponse(response?.ToString());

        if (parseResult.SelectedNames.Count == 0)
        {
            return new ToolSelectionDecision([], true, parseResult.Rationale ?? "Selector did not select any tools.");
        }

        var selectedTools = availableTools
            .Where(tool => parseResult.SelectedNames.Contains(tool.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (selectedTools.Count == 0)
        {
            return new ToolSelectionDecision([], true, parseResult.Rationale ?? "Selector response did not match any eligible tools.");
        }

        return new ToolSelectionDecision(selectedTools, true, parseResult.Rationale);
    }

    private static string BuildToolSelectionInstructions(AppConfig config, JobDefinition job, int toolCount)
    {
        return string.Join(
            Environment.NewLine,
            "You are a preflight tool-selection agent.",
            "Load and apply the tool-selection-optimizer skill before deciding anything.",
            "Your only job is to choose the smallest sufficient subset of tools for the next execution agent call.",
            "Do not solve the task itself.",
            "Do not call tools.",
            $"Available tool count: {toolCount}.",
            job.AutoApprove
                ? "Mutation tools exist, but only select them when the task clearly requires mutation."
                : "Mutation tools are unavailable, so prefer inspection-only planning.",
            $"Requested reasoning level: {job.ResolveThinkingLevel(config)}.",
            "Return one selected tool per line using exactly: TOOL: <tool-name>.",
            "If no tools are needed, return exactly: TOOL: NONE.",
            "Optionally end with one RATIONALE line.");
    }

    private static string BuildExecutionInstructions(AppConfig config, JobDefinition job, int totalToolCount, IReadOnlyList<ToolDefinition> selectedTools, IReadOnlyList<string> allowedTools)
    {
        var selectedToolNames = selectedTools.Count == 0
            ? "none"
            : string.Join(", ", selectedTools.Select(tool => tool.Name));
        var allowlistText = allowedTools.Count == 0
            ? "none"
            : string.Join(", ", allowedTools);

        return string.Join(
            Environment.NewLine,
            "You are an automation agent executing a single one-shot job.",
            "Load and use the available skills when their domain matches the task.",
            "A separate selector pass already narrowed the tool set for this run.",
            $"Total available tools before selection: {totalToolCount}.",
            $"Configured tool allowlist: {allowlistText}.",
            $"Selected tools for this run: {selectedToolNames}.",
            "Only the selected tools are registered for this agent.",
            "Use concrete tools only for actions that skills cannot perform directly.",
            "Do not call tools speculatively or 'just in case'.",
            "Keep tool use minimal and expand the plan through reasoning before acting.",
            "RunCommand and RunDotNetCommand execute a process directly without shell syntax, so do not rely on pipes, redirection, shell built-ins, or &&.",
            "Never claim that a file-system change happened unless a tool call actually succeeded.",
            job.AutoApprove
                ? "Mutation tools are available only if they survived the selector pass. Keep changes minimal and deterministic."
                : "Mutation tools are not available. If the task requires changes, inspect the environment and return a concrete action plan instead.",
            $"Requested reasoning level: {job.ResolveThinkingLevel(config)}.",
            "If you are blocked because an omitted tool would be required, say so explicitly instead of improvising.",
            "Finish with a concise execution summary.");
    }

    private static string BuildToolSelectionPrompt(AppConfig config, JobDefinition job, IReadOnlyList<ToolDefinition> availableTools)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Select the minimum sufficient tool subset for the next agent call.");
        builder.AppendLine($"Job: {job.Name}");
        builder.AppendLine($"Provider: {job.Provider}");
        builder.AppendLine($"Requested reasoning level: {job.ResolveThinkingLevel(config)}");
        builder.AppendLine($"Mutation tools available: {(job.AutoApprove ? "yes" : "no")}");
        builder.AppendLine();
        builder.AppendLine("Task:");
        builder.AppendLine(job.Prompt.Trim());
        builder.AppendLine();
        builder.AppendLine("Available tools:");

        foreach (var tool in availableTools)
        {
            builder.AppendLine($"- {tool.Name} | {(tool.RequiresMutation ? "mutation" : "inspection")} | {tool.Description}");
        }

        builder.AppendLine();
        builder.AppendLine("Return only TOOL lines and an optional final RATIONALE line.");
        return builder.ToString().TrimEnd();
    }

    private static ToolSelectionParseResult ParseToolSelectionResponse(string? response)
    {
        var selectedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? rationale = null;

        if (string.IsNullOrWhiteSpace(response))
        {
            return new ToolSelectionParseResult(selectedTools, rationale);
        }

        foreach (var rawLine in response.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var line = rawLine.TrimStart('-', '*', ' ');
            if (line.StartsWith("RATIONALE:", StringComparison.OrdinalIgnoreCase))
            {
                rationale = line[10..].Trim();
                continue;
            }

            if (!line.StartsWith("TOOL:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = line[5..].Trim();
            if (value.Equals("NONE", StringComparison.OrdinalIgnoreCase))
            {
                return new ToolSelectionParseResult(new HashSet<string>(StringComparer.OrdinalIgnoreCase), rationale);
            }

            foreach (var toolName in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                selectedTools.Add(toolName);
            }
        }

        return new ToolSelectionParseResult(selectedTools, rationale);
    }

    private static IReadOnlyList<ToolDefinition> ApplyAllowlist(JobDefinition job, IReadOnlyList<ToolDefinition> availableTools)
    {
        if (job.AllowedTools.Count == 0)
        {
            return availableTools;
        }

        var allowlist = new HashSet<string>(job.AllowedTools, StringComparer.OrdinalIgnoreCase);
        return availableTools
            .Where(tool => allowlist.Contains(tool.Name))
            .ToList();
    }

    private static IReadOnlyList<ToolDefinition> BuildToolDefinitions(JobDefinition job, string configDirectory)
    {
        var fileSystemTools = new FileSystemTools();
        var processTools = new ProcessTools(configDirectory);
        var tools = new List<ToolDefinition>
        {
            new("GetKnownFolder", "Resolve a well-known path such as home, desktop, documents, downloads, or temp.", false, () => AIFunctionFactory.Create(fileSystemTools.GetKnownFolder)),
            new("ListDirectory", "Inspect a directory before reading, moving, or deleting items inside it.", false, () => AIFunctionFactory.Create(fileSystemTools.ListDirectory)),
            new("ReadTextFile", "Read a UTF-8 text file when the task depends on file contents.", false, () => AIFunctionFactory.Create(fileSystemTools.ReadTextFile)),
            new("ReadTextFileLines", "Read a specific inclusive line range from a UTF-8 text file when the file is too large for a full read.", false, () => AIFunctionFactory.Create(fileSystemTools.ReadTextFileLines)),
            new("GetTextFileLength", "Inspect a text file's character, line, and UTF-8 byte counts before planning chunked reads.", false, () => AIFunctionFactory.Create(fileSystemTools.GetTextFileLength)),
        };

        if (job.AutoApprove)
        {
            tools.Add(new("CreateDirectory", "Create a directory when the target structure must exist.", true, () => AIFunctionFactory.Create(fileSystemTools.CreateDirectory)));
            tools.Add(new("MoveFile", "Move a file to a new location.", true, () => AIFunctionFactory.Create(fileSystemTools.MoveFile)));
            tools.Add(new("MoveFiles", "Move multiple files in parallel for faster batch operations.", true, () => AIFunctionFactory.Create(fileSystemTools.MoveFiles)));
            tools.Add(new("CopyFile", "Copy a file to a new location.", true, () => AIFunctionFactory.Create(fileSystemTools.CopyFile)));
            tools.Add(new("DeleteFile", "Delete a file when removal is explicitly required.", true, () => AIFunctionFactory.Create(fileSystemTools.DeleteFile)));
            tools.Add(new("WriteTextFile", "Create or overwrite a UTF-8 text file when the task requires writing content.", true, () => AIFunctionFactory.Create(fileSystemTools.WriteTextFile)));
            tools.Add(new("RunCommand", "Run an installed executable directly without a shell when the task needs scriptable or tool-driven automation.", true, () => AIFunctionFactory.Create(processTools.RunCommand)));
            tools.Add(new("RunDotNetCommand", "Run a dotnet CLI command for .NET and C# automation without a shell.", true, () => AIFunctionFactory.Create(processTools.RunDotNetCommand)));
        }

        return tools;
    }

    private sealed record ToolDefinition(string Name, string Description, bool RequiresMutation, Func<AITool> CreateTool);

    private sealed record ToolSelectionDecision(IReadOnlyList<ToolDefinition> SelectedTools, bool SelectorUsed, string? Rationale);

    private sealed record ToolSelectionParseResult(HashSet<string> SelectedNames, string? Rationale);

    #pragma warning disable MAAI001
    private static AIContextProvider CreateSkillsProvider(string configDirectory)
    {
        var skillPaths = GetSkillPaths(configDirectory);

        return skillPaths.Count switch
        {
            0 => throw new InvalidOperationException("No skill directories are available."),
            1 => new FileAgentSkillsProvider(skillPaths[0], options: null, loggerFactory: null),
            _ => new FileAgentSkillsProvider(skillPaths, options: null, loggerFactory: null),
        };
    }
    #pragma warning restore MAAI001

    private static List<string> GetSkillPaths(string configDirectory)
    {
        var candidates = new[]
        {
            Path.Combine(configDirectory, "skills"),
            Path.Combine(AppContext.BaseDirectory, "skills"),
        };

        return candidates
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}