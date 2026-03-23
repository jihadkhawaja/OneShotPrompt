namespace OneShotPrompt.Core.Models;

public sealed class ToolSelectionSummary
{
    public int TotalAvailableTools { get; init; }

    public int EligibleTools { get; init; }

    public bool SelectorUsed { get; init; }

    public List<string> AllowedTools { get; init; } = [];

    public List<string> SelectedTools { get; init; } = [];

    public string Workflow { get; init; } = "single-agent";

    public List<GeneratedAgentSummary> GeneratedAgents { get; init; } = [];

    public string? Rationale { get; init; }
}

public sealed class GeneratedAgentSummary
{
    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public List<string> AssignedTools { get; init; } = [];
}