using System.Diagnostics.CodeAnalysis;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.GitHub.Copilot;
using OneShotPrompt.Application.Abstractions;

namespace OneShotPrompt.Infrastructure.Providers;

[ExcludeFromCodeCoverage]
internal sealed class AgentFrameworkJobAgent(AIAgent agent) : IJobAgent, IAsyncDisposable, IDisposable
{
    public async Task<string> RunAsync(string prompt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var response = await agent.RunAsync(prompt);
        return NormalizeResponseText(response?.ToString(), agent is GitHubCopilotAgent);
    }

    private static string NormalizeResponseText(string? responseText, bool isGitHubCopilot)
    {
        var trimmed = responseText?.Trim() ?? string.Empty;
        if (!isGitHubCopilot || string.IsNullOrEmpty(trimmed))
        {
            return trimmed;
        }

        for (var unitLength = 1; unitLength <= trimmed.Length / 2; unitLength++)
        {
            if (trimmed.Length % unitLength != 0)
            {
                continue;
            }

            var repeatedCount = trimmed.Length / unitLength;
            if (repeatedCount < 2)
            {
                continue;
            }

            var candidate = trimmed[..unitLength];
            if (candidate.Length < 16 && !candidate.Any(char.IsWhiteSpace))
            {
                continue;
            }

            var matches = true;
            for (var index = unitLength; index < trimmed.Length; index += unitLength)
            {
                if (!trimmed.AsSpan(index, unitLength).SequenceEqual(candidate))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return candidate.Trim();
            }
        }

        return trimmed;
    }

    public ValueTask DisposeAsync()
    {
        if (agent is IAsyncDisposable asyncDisposable)
        {
            return asyncDisposable.DisposeAsync();
        }

        if (agent is IDisposable disposable)
        {
            disposable.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        if (agent is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}