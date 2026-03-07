using Anthropic;
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
    public Task<IJobAgent> CreateAsync(AppConfig config, JobDefinition job, CancellationToken cancellationToken)
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
            JobProvider.OpenAI => CreateOpenAIAgent(config, job, instructions, tools),
            JobProvider.Anthropic => CreateAnthropicAgent(config, job, instructions, tools),
            JobProvider.OpenAICompatible => CreateOpenAICompatibleAgent(config, job, instructions, tools),
            _ => throw new InvalidOperationException($"Unsupported provider '{provider}'."),
        };

        return Task.FromResult<IJobAgent>(new AgentFrameworkJobAgent(agent));
    }

    private static Microsoft.Agents.AI.AIAgent CreateOpenAIAgent(AppConfig config, JobDefinition job, string instructions, IList<AITool> tools)
    {
        var client = new OpenAIClient(config.OpenAI.ApiKey);
        var chatClient = client.GetChatClient(config.OpenAI.Model).AsIChatClient();

        return chatClient.AsAIAgent(
            name: job.Name,
            instructions: instructions,
            tools: tools);
    }

    private static Microsoft.Agents.AI.AIAgent CreateOpenAICompatibleAgent(AppConfig config, JobDefinition job, string instructions, IList<AITool> tools)
    {
        var chatClient = new ChatClient(
            model: config.OpenAICompatible.Model,
            credential: new ApiKeyCredential(config.OpenAICompatible.ApiKey),
            options: new OpenAIClientOptions
            {
                Endpoint = new Uri(config.OpenAICompatible.Endpoint),
            }).AsIChatClient();

        return chatClient.AsAIAgent(
            name: job.Name,
            instructions: instructions,
            tools: tools);
    }

    private static Microsoft.Agents.AI.AIAgent CreateAnthropicAgent(AppConfig config, JobDefinition job, string instructions, IList<AITool> tools)
    {
        var client = new AnthropicClient
        {
            ApiKey = config.Anthropic.ApiKey,
        };

        return client.AsIChatClient(config.Anthropic.Model).AsAIAgent(
            name: job.Name,
            instructions: instructions,
            tools: tools);
    }

    private static string BuildInstructions(AppConfig config, JobDefinition job)
    {
        return string.Join(
            Environment.NewLine,
            "You are an automation agent executing a single one-shot job.",
            "Use the available tools when an action is required.",
            "Never claim that a file-system change happened unless a tool call actually succeeded.",
            job.AutoApprove
                ? "Mutation tools are available. Keep changes minimal and deterministic."
                : "Mutation tools are not available. If the task requires changes, inspect the environment and return a concrete action plan instead.",
            $"Requested reasoning level: {job.ResolveThinkingLevel(config)}.",
            "Finish with a concise execution summary.");
    }

    private static IList<AITool> BuildTools(JobDefinition job)
    {
        var toolbox = new FileSystemToolbox();
        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(toolbox.GetKnownFolder),
            AIFunctionFactory.Create(toolbox.ListDirectory),
            AIFunctionFactory.Create(toolbox.ReadTextFile),
        };

        if (job.AutoApprove)
        {
            tools.Add(AIFunctionFactory.Create(toolbox.CreateDirectory));
            tools.Add(AIFunctionFactory.Create(toolbox.MoveFile));
            tools.Add(AIFunctionFactory.Create(toolbox.CopyFile));
            tools.Add(AIFunctionFactory.Create(toolbox.DeleteFile));
            tools.Add(AIFunctionFactory.Create(toolbox.WriteTextFile));
        }

        return tools;
    }
}