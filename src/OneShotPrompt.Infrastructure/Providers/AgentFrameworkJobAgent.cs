using Microsoft.Agents.AI;
using OneShotPrompt.Application.Abstractions;

namespace OneShotPrompt.Infrastructure.Providers;

internal sealed class AgentFrameworkJobAgent(AIAgent agent) : IJobAgent
{
    public async Task<string> RunAsync(string prompt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var response = await agent.RunAsync(prompt);
        return response?.ToString()?.Trim() ?? string.Empty;
    }
}