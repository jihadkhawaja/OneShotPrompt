using System.ClientModel;
using System.Text;
using Anthropic;
using GeminiDotnet;
using GeminiDotnet.Extensions.AI;
using GitHub.Copilot.SDK;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.GitHub.Copilot;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using OneShotPrompt.Application.Abstractions;
using OneShotPrompt.Core.Enums;
using OneShotPrompt.Core.Models;
using OneShotPrompt.Infrastructure.Tools;
using OpenAI;
using OpenAI.Chat;

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
        var selection = provider switch
        {
            JobProvider.GitHubCopilot => await SelectToolsWithGitHubCopilotAsync(config, job, configDirectory, eligibleTools, cancellationToken),
            _ => await SelectToolsAsync(CreateChatClient(config, provider), config, job, configDirectory, eligibleTools, cancellationToken),
        };
        IJobAgent jobAgent;
        IReadOnlyList<GeneratedAgentSummary> generatedAgents = [];

        if (job.UsesCorporatePlanning())
        {
            var planningBuild = await CreateCorporatePlanningAgentAsync(
                config,
                provider,
                job,
                configDirectory,
                selection.SelectedTools,
                cancellationToken);
            jobAgent = planningBuild.Agent;
            generatedAgents = planningBuild.GeneratedAgents;
        }
        else
        {
            var instructions = BuildExecutionInstructions(config, job, availableTools.Count, selection.SelectedTools, job.AllowedTools);
            var selectedFunctions = CreateFunctionTools(selection.SelectedTools);

            var agent = provider switch
            {
                JobProvider.GitHubCopilot => CreateGitHubCopilotAgent(
                    config.GitHubCopilot,
                    job.Name,
                    selectedFunctions,
                    instructions,
                    configDirectory),
                _ => CreateChatClientAgent(config, provider, job.Name, selectedFunctions, instructions, configDirectory),
            };

            jobAgent = new AgentFrameworkJobAgent(agent);
        }

        return new PreparedJobAgent(
            jobAgent,
            new ToolSelectionSummary
            {
                TotalAvailableTools = availableTools.Count,
                EligibleTools = eligibleTools.Count,
                SelectorUsed = selection.SelectorUsed,
                AllowedTools = [.. job.AllowedTools],
                SelectedTools = [.. selection.SelectedTools.Select(tool => tool.Name)],
                Workflow = job.ResolveWorkflow(),
                GeneratedAgents = [.. generatedAgents],
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
            JobProvider.Gemini => new GeminiChatClient(new GeminiClientOptions
            {
                ApiKey = config.Gemini.ApiKey,
                ModelId = config.Gemini.Model,
            }),
            JobProvider.OpenAICompatible => new OpenAICompatibleChatClient(
                new ChatClient(
                    model: config.OpenAICompatible.Model,
                    credential: new ApiKeyCredential(config.OpenAICompatible.ApiKey),
                    options: new OpenAIClientOptions
                    {
                        Endpoint = new Uri(config.OpenAICompatible.Endpoint),
                    }).AsIChatClient()),
            JobProvider.Anthropic => new AnthropicClient
            {
                ApiKey = config.Anthropic.ApiKey,
            }.AsIChatClient(config.Anthropic.Model),
            _ => throw new InvalidOperationException($"Unsupported provider '{provider}'."),
        };
    }

    private AIAgent CreateChatClientAgent(
        AppConfig config,
        JobProvider provider,
        string agentName,
        IList<AIFunction> tools,
        string instructions,
        string configDirectory)
    {
        var chatClient = CreateChatClient(config, provider);

        IChatClient executionClient = eventSink is not null
            ? new ObservableChatClient(chatClient, eventSink)
            : chatClient;

        return CreateAgent(
            executionClient,
            agentName,
            tools.Cast<AITool>().ToList(),
            instructions,
            configDirectory);
    }

    private static AIAgent CreateGitHubCopilotAgent(
        GitHubCopilotProviderSettings settings,
        string agentName,
        IList<AIFunction> tools,
        string instructions,
        string configDirectory)
    {
        var client = new CopilotClient(CreateGitHubCopilotClientOptions(settings, configDirectory));

        return new GitHubCopilotAgent(
            client,
            CreateGitHubCopilotSessionConfig(settings, tools, instructions, configDirectory),
            ownsClient: true,
            name: agentName,
            description: $"OneShotPrompt agent for job '{agentName}'.");
    }

    private static CopilotClientOptions CreateGitHubCopilotClientOptions(GitHubCopilotProviderSettings settings, string configDirectory)
    {
        var options = new CopilotClientOptions
        {
            AutoStart = settings.AutoStart,
            AutoRestart = settings.AutoRestart,
            Cwd = configDirectory,
            LogLevel = string.IsNullOrWhiteSpace(settings.LogLevel) ? "info" : settings.LogLevel,
        };

        if (!string.IsNullOrWhiteSpace(settings.CliPath))
        {
            options.CliPath = settings.CliPath;
        }

        if (!string.IsNullOrWhiteSpace(settings.CliUrl))
        {
            options.CliUrl = settings.CliUrl;
        }

        if (!string.IsNullOrWhiteSpace(settings.GitHubToken))
        {
            options.GitHubToken = settings.GitHubToken;
        }

        if (settings.UseLoggedInUser.HasValue)
        {
            options.UseLoggedInUser = settings.UseLoggedInUser.Value;
        }

        return options;
    }

    private static SessionConfig CreateGitHubCopilotSessionConfig(
        GitHubCopilotProviderSettings settings,
        IList<AIFunction> tools,
        string instructions,
        string configDirectory)
    {
        var skillPaths = GetSkillPaths(configDirectory);
        if (skillPaths.Count == 0)
        {
            throw new InvalidOperationException("No skill directories are available.");
        }

        return new SessionConfig
        {
            ClientName = "OneShotPrompt",
            ConfigDir = configDirectory,
            Model = string.IsNullOrWhiteSpace(settings.Model) ? null : settings.Model,
            OnPermissionRequest = DenyGitHubCopilotPermissionRequestAsync,
            SkillDirectories = skillPaths,
            SystemMessage = new SystemMessageConfig
            {
                Mode = SystemMessageMode.Append,
                Content = instructions,
            },
            Tools = tools,
            WorkingDirectory = configDirectory,
        };
    }

    private static Task<PermissionRequestResult> DenyGitHubCopilotPermissionRequestAsync(PermissionRequest _, PermissionInvocation __)
    {
        return Task.FromResult(new PermissionRequestResult
        {
            Kind = "denied-no-approval-rule-and-could-not-request-from-user",
        });
    }

    private static List<AIFunction> CreateFunctionTools(IReadOnlyList<ToolDefinition> selectedTools)
    {
        var functions = new List<AIFunction>(selectedTools.Count);

        foreach (var tool in selectedTools)
        {
            if (tool.CreateTool() is not AIFunction function)
            {
                throw new InvalidOperationException($"Tool '{tool.Name}' is not compatible with the GitHub Copilot provider.");
            }

            functions.Add(function);
        }

        return functions;
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

    private async Task<CorporatePlanningBuildResult> CreateCorporatePlanningAgentAsync(
        AppConfig config,
        JobProvider provider,
        JobDefinition job,
        string configDirectory,
        IReadOnlyList<ToolDefinition> selectedTools,
        CancellationToken cancellationToken)
    {
        var participantSpecifications = provider switch
        {
            JobProvider.GitHubCopilot => await DesignCorporatePlanningParticipantsWithGitHubCopilotAsync(
                config,
                job,
                configDirectory,
                selectedTools,
                cancellationToken),
            _ => await DesignCorporatePlanningParticipantsAsync(
                CreateChatClient(config, provider),
                config,
                job,
                configDirectory,
                selectedTools,
                cancellationToken),
        };

        var participants = new List<AIAgent>(participantSpecifications.Count);
        var generatedAgents = new List<GeneratedAgentSummary>(participantSpecifications.Count);

        for (var index = 0; index < participantSpecifications.Count; index++)
        {
            var specification = participantSpecifications[index];
            var resolvedName = string.IsNullOrWhiteSpace(specification.Name)
                ? $"PlanningAgent{index + 1}"
                : specification.Name.Trim();
            var assignedTools = ResolveAssignedTools(selectedTools, specification.AssignedTools);
            var functions = CreateFunctionTools(assignedTools);
            var instructions = BuildCorporatePlanningParticipantInstructions(job, specification, assignedTools, isFinalizer: index == 0);

            var participant = provider switch
            {
                JobProvider.GitHubCopilot => CreateGitHubCopilotAgent(
                    config.GitHubCopilot,
                    resolvedName,
                    functions,
                    instructions,
                    configDirectory),
                _ => CreateChatClientAgent(config, provider, resolvedName, functions, instructions, configDirectory),
            };

            participants.Add(participant);
            generatedAgents.Add(new GeneratedAgentSummary
            {
                Name = resolvedName,
                Description = specification.Description,
                AssignedTools = [.. assignedTools.Select(tool => tool.Name)],
            });
        }

        var workflow = AgentWorkflowBuilder
            .CreateGroupChatBuilderWith(agents => new RoundRobinGroupChatManager(agents, ShouldTerminateCorporatePlanningAsync)
            {
                MaximumIterationCount = config.CorporatePlanning.MaxIterations,
            })
            .AddParticipants(participants.ToArray())
            .Build();

        return new CorporatePlanningBuildResult(new CorporatePlanningJobAgent(workflow, participants, eventSink), generatedAgents);
    }

    private static async Task<IReadOnlyList<PlanningParticipantSpecification>> DesignCorporatePlanningParticipantsAsync(
        IChatClient chatClient,
        AppConfig config,
        JobDefinition job,
        string configDirectory,
        IReadOnlyList<ToolDefinition> selectedTools,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var architect = CreateAgent(
            chatClient,
            $"{job.Name}-planning-architect",
            [],
            BuildCorporatePlanningArchitectInstructions(config, job, selectedTools.Count),
            configDirectory);

        var response = await architect.RunAsync(BuildCorporatePlanningArchitectPrompt(config, job, selectedTools));
        return ParseCorporatePlanningParticipants(response?.ToString(), config, selectedTools);
    }

    private static async Task<IReadOnlyList<PlanningParticipantSpecification>> DesignCorporatePlanningParticipantsWithGitHubCopilotAsync(
        AppConfig config,
        JobDefinition job,
        string configDirectory,
        IReadOnlyList<ToolDefinition> selectedTools,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using var architect = new AgentFrameworkJobAgent(CreateGitHubCopilotAgent(
            config.GitHubCopilot,
            $"{job.Name}-planning-architect",
            [],
            BuildCorporatePlanningArchitectInstructions(config, job, selectedTools.Count),
            configDirectory));

        var response = await architect.RunAsync(BuildCorporatePlanningArchitectPrompt(config, job, selectedTools), cancellationToken);
        return ParseCorporatePlanningParticipants(response, config, selectedTools);
    }

    private static string BuildCorporatePlanningArchitectInstructions(AppConfig config, JobDefinition job, int selectedToolCount)
    {
        var lines = new List<string>
        {
            "You are designing a temporary corporate-planning team for a single task.",
            "Do not solve the task.",
            $"Return between 2 and {config.CorporatePlanning.MaxAgents} agent blocks.",
            "Put the final synthesizer first.",
            "The first agent must be able to conclude with FINAL_RESPONSE: when the team converges.",
            "Avoid redundant roles.",
            "Only assign tools from the provided shortlist.",
            "Use TOOLS: NONE when a role should not have tools.",
            $"Selected tool shortlist size: {selectedToolCount}.",
            "Return each agent in exactly this format:",
            "AGENT: <name>",
            "DESCRIPTION: <one sentence>",
            "TOOLS: <comma separated tool names or NONE>",
            "INSTRUCTIONS: <single paragraph tailored to the role>",
            "END_AGENT",
        };

        if (!IsGitHubCopilotJob(job))
        {
            lines.Insert(8, $"Requested reasoning level: {job.ResolveThinkingLevel(config)}.");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildCorporatePlanningArchitectPrompt(AppConfig config, JobDefinition job, IReadOnlyList<ToolDefinition> selectedTools)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Design the smallest effective corporate-planning team for this task.");
        builder.AppendLine($"Job: {job.Name}");
        builder.AppendLine($"Provider: {job.Provider}");
        builder.AppendLine($"Workflow: {job.ResolveWorkflow()}");
        builder.AppendLine($"Max agents: {config.CorporatePlanning.MaxAgents}");
        builder.AppendLine($"Max iterations: {config.CorporatePlanning.MaxIterations}");
        builder.AppendLine($"Mutation tools available: {(job.AutoApprove ? "yes" : "no")}");
        builder.AppendLine();
        builder.AppendLine("Task:");
        builder.AppendLine(job.Prompt.Trim());
        builder.AppendLine();
        builder.AppendLine("Selected tools available to distribute:");

        if (selectedTools.Count == 0)
        {
            builder.AppendLine("- NONE");
        }
        else
        {
            foreach (var tool in selectedTools)
            {
                builder.AppendLine($"- {tool.Name} | {(tool.RequiresMutation ? "mutation" : "inspection")} | {tool.Description}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Return only AGENT blocks.");
        return builder.ToString().TrimEnd();
    }

    private static IReadOnlyList<PlanningParticipantSpecification> ParseCorporatePlanningParticipants(
        string? response,
        AppConfig config,
        IReadOnlyList<ToolDefinition> selectedTools)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return CreateFallbackCorporatePlanningParticipants(config, selectedTools);
        }

        var participants = new List<PlanningParticipantSpecification>();
        string? name = null;
        string? description = null;
        string? instructions = null;
        List<string> assignedTools = [];

        void FlushCurrent()
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            participants.Add(new PlanningParticipantSpecification(
                name.Trim(),
                string.IsNullOrWhiteSpace(description) ? "Supports the planning discussion." : description.Trim(),
                string.IsNullOrWhiteSpace(instructions) ? "Contribute concise, role-specific planning guidance." : instructions.Trim(),
                [.. assignedTools]));

            name = null;
            description = null;
            instructions = null;
            assignedTools = [];
        }

        foreach (var rawLine in response.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var line = rawLine.Trim();

            if (line.StartsWith("AGENT:", StringComparison.OrdinalIgnoreCase))
            {
                FlushCurrent();
                name = line[6..].Trim();
                continue;
            }

            if (line.StartsWith("DESCRIPTION:", StringComparison.OrdinalIgnoreCase))
            {
                description = line[12..].Trim();
                continue;
            }

            if (line.StartsWith("TOOLS:", StringComparison.OrdinalIgnoreCase))
            {
                assignedTools = ParseAssignedTools(line[6..]);
                continue;
            }

            if (line.StartsWith("INSTRUCTIONS:", StringComparison.OrdinalIgnoreCase))
            {
                instructions = line[13..].Trim();
                continue;
            }

            if (line.Equals("END_AGENT", StringComparison.OrdinalIgnoreCase))
            {
                FlushCurrent();
            }
        }

        FlushCurrent();

        return participants.Count >= 2
            ? [.. participants.Take(config.CorporatePlanning.MaxAgents)]
            : CreateFallbackCorporatePlanningParticipants(config, selectedTools);
    }

    private static List<string> ParseAssignedTools(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Equals("NONE", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(tool => tool.Trim())
            .Where(tool => !string.IsNullOrWhiteSpace(tool))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<PlanningParticipantSpecification> CreateFallbackCorporatePlanningParticipants(
        AppConfig config,
        IReadOnlyList<ToolDefinition> selectedTools)
    {
        var inspectionTools = selectedTools
            .Where(tool => !tool.RequiresMutation)
            .Select(tool => tool.Name)
            .ToList();
        var mutationTools = selectedTools
            .Where(tool => tool.RequiresMutation)
            .Select(tool => tool.Name)
            .ToList();

        var participants = new List<PlanningParticipantSpecification>
        {
            new(
                "Planning Lead",
                "Coordinates the conversation and owns the final response.",
                "Synthesize the discussion into the exact final payload the task requires, whether that is a plan, a sentence, or another concrete deliverable.",
                []),
            new(
                "Task Analyst",
                "Inspects the task, context, and constraints.",
                "Gather missing facts, question ambiguous assumptions, and ground the team in concrete evidence.",
                inspectionTools),
            new(
                "Risk Reviewer",
                "Challenges weak assumptions and validates the plan.",
                "Focus on failure modes, missing tools, unsafe steps, and whether the plan is actually executable.",
                []),
        };

        if (mutationTools.Count > 0 && participants.Count < config.CorporatePlanning.MaxAgents)
        {
            participants.Insert(2, new PlanningParticipantSpecification(
                "Operations Planner",
                "Designs the change sequence for any required commands or file mutations.",
                "Translate the approved direction into minimal, deterministic execution steps and call out rollback needs.",
                mutationTools));
        }

        return [.. participants.Take(config.CorporatePlanning.MaxAgents)];
    }

    private static IReadOnlyList<ToolDefinition> ResolveAssignedTools(
        IReadOnlyList<ToolDefinition> selectedTools,
        IReadOnlyList<string> assignedToolNames)
    {
        if (assignedToolNames.Count == 0)
        {
            return [];
        }

        var allowlist = new HashSet<string>(assignedToolNames, StringComparer.OrdinalIgnoreCase);
        return [.. selectedTools.Where(tool => allowlist.Contains(tool.Name))];
    }

    private static string BuildCorporatePlanningParticipantInstructions(
        JobDefinition job,
        PlanningParticipantSpecification specification,
        IReadOnlyList<ToolDefinition> assignedTools,
        bool isFinalizer)
    {
        var lines = new List<string>
        {
            $"You are {specification.Name}.",
            specification.Description,
            "You are collaborating in a corporate-planning group chat to complete one task.",
            "Read the full shared conversation history before responding.",
            specification.Instructions,
            $"Task: {job.Prompt.Trim()}",
            assignedTools.Count == 0
                ? "You have no tools. Work from the shared discussion and explicitly call out any missing capability."
                : $"You may use only these tools: {string.Join(", ", assignedTools.Select(tool => tool.Name))}.",
            isFinalizer
                ? "You own final synthesis. When the team has converged, respond with 'FINAL_RESPONSE:' followed by the exact final payload the user should receive. If the task calls for a plan, emit the plan as that payload."
                : "Do not emit FINAL_RESPONSE. Contribute role-specific analysis, corrections, or validation only.",
            "Keep responses concise, concrete, and execution-oriented.",
        };

        return string.Join(Environment.NewLine, lines);
    }

    private static ValueTask<bool> ShouldTerminateCorporatePlanningAsync(
        RoundRobinGroupChatManager _,
        IEnumerable<Microsoft.Extensions.AI.ChatMessage> history,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var latestMessage = history.LastOrDefault(message => !string.IsNullOrWhiteSpace(message.Text));
        var shouldTerminate = latestMessage?.Text?.Contains("FINAL_RESPONSE:", StringComparison.OrdinalIgnoreCase) == true ||
            latestMessage?.Text?.Contains("FINAL_PLAN:", StringComparison.OrdinalIgnoreCase) == true ||
            latestMessage?.Text?.Contains("PLAN_COMPLETE", StringComparison.OrdinalIgnoreCase) == true ||
            latestMessage?.Text?.Contains("TASK_COMPLETE", StringComparison.OrdinalIgnoreCase) == true;

        return ValueTask.FromResult(shouldTerminate);
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

    private static async Task<ToolSelectionDecision> SelectToolsWithGitHubCopilotAsync(
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

        await using var selector = new AgentFrameworkJobAgent(CreateGitHubCopilotAgent(
            config.GitHubCopilot,
            $"{job.Name}-tool-selector",
            [],
            BuildToolSelectionInstructions(config, job, availableTools.Count),
            configDirectory));

        var response = await selector.RunAsync(BuildToolSelectionPrompt(config, job, availableTools), cancellationToken);
        var parseResult = ParseToolSelectionResponse(response);

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
        var lines = new List<string>
        {
            "You are a preflight tool-selection agent.",
            "Load and apply the tool-selection-optimizer skill before deciding anything.",
            "Your only job is to choose the smallest sufficient subset of tools for the next execution agent call.",
            "Do not solve the task itself.",
            "Do not call tools.",
            $"Available tool count: {toolCount}.",
            job.AutoApprove
                ? "Mutation tools exist, but only select them when the task clearly requires mutation."
                : "Mutation tools are unavailable, so prefer inspection-only planning.",
            "Return one selected tool per line using exactly: TOOL: <tool-name>.",
            "If no tools are needed, return exactly: TOOL: NONE.",
            "Optionally end with one RATIONALE line.",
        };

        if (!IsGitHubCopilotJob(job))
        {
            lines.Insert(7, $"Requested reasoning level: {job.ResolveThinkingLevel(config)}.");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildExecutionInstructions(AppConfig config, JobDefinition job, int totalToolCount, IReadOnlyList<ToolDefinition> selectedTools, IReadOnlyList<string> allowedTools)
    {
        var selectedToolNames = selectedTools.Count == 0
            ? "none"
            : string.Join(", ", selectedTools.Select(tool => tool.Name));
        var allowlistText = allowedTools.Count == 0
            ? "none"
            : string.Join(", ", allowedTools);
        var lines = new List<string>
        {
            "You are an automation agent executing a single one-shot job.",
            "Load and use the available skills when their domain matches the task.",
            "A separate selector pass already narrowed the tool set for this run.",
            $"Total available tools before selection: {totalToolCount}.",
            $"Configured tool allowlist: {allowlistText}.",
            $"Selected tools for this run: {selectedToolNames}.",
            "Only the selected tools are registered for this agent.",
            "Use concrete tools only for actions that skills cannot perform directly.",
            "Do not call tools speculatively or 'just in case'.",
            IsGitHubCopilotJob(job)
                ? "Keep tool use minimal before acting."
                : "Keep tool use minimal and expand the plan through reasoning before acting.",
            "RunCommand and RunDotNetCommand execute a process directly without shell syntax, so do not rely on pipes, redirection, shell built-ins, or &&.",
            "Never claim that a file-system change happened unless a tool call actually succeeded.",
            job.AutoApprove
                ? "Mutation tools are available only if they survived the selector pass. Keep changes minimal and deterministic."
                : "Mutation tools are not available. If the task requires changes, inspect the environment and return a concrete action plan instead.",
            "If you are blocked because an omitted tool would be required, say so explicitly instead of improvising.",
            "Finish with a concise execution summary.",
        };

        if (!IsGitHubCopilotJob(job))
        {
            lines.Insert(13, $"Requested reasoning level: {job.ResolveThinkingLevel(config)}.");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildToolSelectionPrompt(AppConfig config, JobDefinition job, IReadOnlyList<ToolDefinition> availableTools)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Select the minimum sufficient tool subset for the next agent call.");
        builder.AppendLine($"Job: {job.Name}");
        builder.AppendLine($"Provider: {job.Provider}");
        if (!IsGitHubCopilotJob(job))
        {
            builder.AppendLine($"Requested reasoning level: {job.ResolveThinkingLevel(config)}");
        }
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

    private static bool IsGitHubCopilotJob(JobDefinition job)
    {
        return job.Provider.Equals(nameof(JobProvider.GitHubCopilot), StringComparison.OrdinalIgnoreCase);
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
        var createTool = CreateToolFactory(job);
        var tools = new List<ToolDefinition>
        {
            new("GetKnownFolder", "Resolve a well-known path such as home, desktop, documents, downloads, or temp.", false, () => createTool(fileSystemTools.GetKnownFolder)),
            new("ListDirectory", "Inspect a directory before reading, moving, or deleting items inside it.", false, () => createTool(fileSystemTools.ListDirectory)),
            new("ReadTextFile", "Read a UTF-8 text file when the task depends on file contents.", false, () => createTool(fileSystemTools.ReadTextFile)),
            new("ReadTextFileLines", "Read a specific inclusive line range from a UTF-8 text file when the file is too large for a full read.", false, () => createTool(fileSystemTools.ReadTextFileLines)),
            new("GetTextFileLength", "Inspect a text file's character, line, and UTF-8 byte counts before planning chunked reads.", false, () => createTool(fileSystemTools.GetTextFileLength)),
        };

        if (job.AutoApprove)
        {
            tools.Add(new("CreateDirectory", "Create a directory when the target structure must exist.", true, () => createTool(fileSystemTools.CreateDirectory)));
            tools.Add(new("MoveFile", "Move a file to a new location.", true, () => createTool(fileSystemTools.MoveFile)));
            tools.Add(new("MoveFiles", "Move multiple files in parallel for faster batch operations.", true, () => createTool(fileSystemTools.MoveFiles)));
            tools.Add(new("CopyFile", "Copy a file to a new location.", true, () => createTool(fileSystemTools.CopyFile)));
            tools.Add(new("DeleteFile", "Delete a file when removal is explicitly required.", true, () => createTool(fileSystemTools.DeleteFile)));
            tools.Add(new("WriteTextFile", "Create or overwrite a UTF-8 text file when the task requires writing content.", true, () => createTool(fileSystemTools.WriteTextFile)));
            tools.Add(new("RunCommand", "Run an installed executable directly without a shell when the task needs scriptable or tool-driven automation.", true, () => createTool(processTools.RunCommand)));
            tools.Add(new("RunDotNetCommand", "Run a dotnet CLI command for .NET and C# automation without a shell.", true, () => createTool(processTools.RunDotNetCommand)));
        }

        return tools;
    }

    private static Func<Delegate, AITool> CreateToolFactory(JobDefinition job)
    {
        if (!job.Provider.Equals(nameof(JobProvider.OpenAICompatible), StringComparison.OrdinalIgnoreCase))
        {
            return static callback => AIFunctionFactory.Create(callback);
        }

        var options = new AIFunctionFactoryOptions
        {
            JsonSchemaCreateOptions = new AIJsonSchemaCreateOptions
            {
                TransformOptions = new AIJsonSchemaTransformOptions
                {
                    UseNullableKeyword = true,
                    MoveDefaultKeywordToDescription = true,
                },
            },
        };

        return callback => AIFunctionFactory.Create(callback, options);
    }

    private sealed record ToolDefinition(string Name, string Description, bool RequiresMutation, Func<AITool> CreateTool);

    private sealed record ToolSelectionDecision(IReadOnlyList<ToolDefinition> SelectedTools, bool SelectorUsed, string? Rationale);

    private sealed record ToolSelectionParseResult(HashSet<string> SelectedNames, string? Rationale);

    private sealed record PlanningParticipantSpecification(string Name, string Description, string Instructions, IReadOnlyList<string> AssignedTools);

    private sealed record CorporatePlanningBuildResult(IJobAgent Agent, IReadOnlyList<GeneratedAgentSummary> GeneratedAgents);

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