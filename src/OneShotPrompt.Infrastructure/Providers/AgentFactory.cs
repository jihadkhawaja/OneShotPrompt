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

namespace OneShotPrompt.Infrastructure.Providers;

public sealed class AgentFactory : IJobAgentFactory
{
    public Task<IJobAgent> CreateAsync(AppConfig config, JobDefinition job, string configDirectory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Enum.TryParse<JobProvider>(job.Provider, ignoreCase: true, out var provider))
        {
            throw new InvalidOperationException($"Unsupported provider '{job.Provider}'.");
        }

        var instructions = BuildInstructions(config, job);
        var tools = BuildTools(job);

        var agent = provider switch
        {
            JobProvider.OpenAI => CreateOpenAIAgent(config, job, instructions, tools, configDirectory),
            JobProvider.Anthropic => CreateAnthropicAgent(config, job, instructions, tools, configDirectory),
            JobProvider.OpenAICompatible => CreateOpenAICompatibleAgent(config, job, instructions, tools, configDirectory),
            _ => throw new InvalidOperationException($"Unsupported provider '{provider}'."),
        };

        return Task.FromResult<IJobAgent>(new AgentFrameworkJobAgent(agent));
    }

    private static AIAgent CreateOpenAIAgent(AppConfig config, JobDefinition job, string instructions, IList<AITool> tools, string configDirectory)
    {
        var client = new OpenAIClient(config.OpenAI.ApiKey);
        var chatClient = client.GetChatClient(config.OpenAI.Model).AsIChatClient();

        return CreateAgent(chatClient, job, tools, instructions, configDirectory);
    }

    private static AIAgent CreateOpenAICompatibleAgent(AppConfig config, JobDefinition job, string instructions, IList<AITool> tools, string configDirectory)
    {
        var chatClient = new ChatClient(
            model: config.OpenAICompatible.Model,
            credential: new ApiKeyCredential(config.OpenAICompatible.ApiKey),
            options: new OpenAIClientOptions
            {
                Endpoint = new Uri(config.OpenAICompatible.Endpoint),
            }).AsIChatClient();

        return CreateAgent(chatClient, job, tools, instructions, configDirectory);
    }

    private static AIAgent CreateAnthropicAgent(AppConfig config, JobDefinition job, string instructions, IList<AITool> tools, string configDirectory)
    {
        var client = new AnthropicClient
        {
            ApiKey = config.Anthropic.ApiKey,
        };

        return CreateAgent(client.AsIChatClient(config.Anthropic.Model), job, tools, instructions, configDirectory);
    }

    private static AIAgent CreateAgent(IChatClient chatClient, JobDefinition job, IList<AITool> tools, string instructions, string configDirectory)
    {
        var options = new ChatClientAgentOptions
        {
            Name = job.Name,
            ChatOptions = new ChatOptions
            {
                Instructions = instructions,
                Tools = tools,
            },
            AIContextProviders = [CreateSkillsProvider(configDirectory)],
        };

        return chatClient.AsAIAgent(options);
    }

    private static string BuildInstructions(AppConfig config, JobDefinition job)
    {
        return string.Join(
            Environment.NewLine,
            "You are an automation agent executing a single one-shot job.",
            "Load and use the available skills when their domain matches the task.",
            "Use concrete tools only for actions that skills cannot perform directly.",
            "Never claim that a file-system change happened unless a tool call actually succeeded.",
            job.AutoApprove
                ? "Mutation tools are available. Keep changes minimal and deterministic."
                : "Mutation tools are not available. If the task requires changes, inspect the environment and return a concrete action plan instead.",
            $"Requested reasoning level: {job.ResolveThinkingLevel(config)}.",
            "Finish with a concise execution summary.");
    }

    private static IList<AITool> BuildTools(JobDefinition job)
    {
        var fileSystemTools = new FileSystemTools();
        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(fileSystemTools.GetKnownFolder),
            AIFunctionFactory.Create(fileSystemTools.ListDirectory),
            AIFunctionFactory.Create(fileSystemTools.ReadTextFile),
        };

        if (job.AutoApprove)
        {
            tools.Add(AIFunctionFactory.Create(fileSystemTools.CreateDirectory));
            tools.Add(AIFunctionFactory.Create(fileSystemTools.MoveFile));
            tools.Add(AIFunctionFactory.Create(fileSystemTools.CopyFile));
            tools.Add(AIFunctionFactory.Create(fileSystemTools.DeleteFile));
            tools.Add(AIFunctionFactory.Create(fileSystemTools.WriteTextFile));
        }

        return tools;
    }

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