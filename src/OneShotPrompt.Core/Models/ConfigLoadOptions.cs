namespace OneShotPrompt.Core.Models;

public sealed class ConfigLoadOptions
{
    public ProviderValidationScope ProviderValidationScope { get; init; } = ProviderValidationScope.AllJobs;

    public IReadOnlyList<string> SelectedJobNames { get; init; } = [];
}

public enum ProviderValidationScope
{
    None,
    EnabledJobs,
    SelectedJobs,
    AllJobs,
}