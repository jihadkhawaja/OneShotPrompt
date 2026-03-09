using OneShotPrompt.Core.Models;

namespace OneShotPrompt.Application.Abstractions;

public sealed class PreparedJobAgent(IJobAgent agent, ToolSelectionSummary toolSelection) : IAsyncDisposable
{
    public IJobAgent Agent { get; } = agent;

    public ToolSelectionSummary ToolSelection { get; } = toolSelection;

    public async ValueTask DisposeAsync()
    {
        if (Agent is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
            return;
        }

        if (Agent is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}