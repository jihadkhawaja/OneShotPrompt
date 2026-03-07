namespace OneShotPrompt.Core.Models;

public sealed class ToolSelectionSummary
{
    public int TotalAvailableTools { get; init; }

    public int EligibleTools { get; init; }

    public bool SelectorUsed { get; init; }

    public List<string> AllowedTools { get; init; } = [];

    public List<string> SelectedTools { get; init; } = [];

    public string? Rationale { get; init; }
}