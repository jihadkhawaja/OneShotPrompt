using OneShotPrompt.Core.Models;

namespace OneShotPrompt.Application.Abstractions;

public sealed class PreparedJobAgent(IJobAgent agent, ToolSelectionSummary toolSelection)
{
    public IJobAgent Agent { get; } = agent;

    public ToolSelectionSummary ToolSelection { get; } = toolSelection;
}