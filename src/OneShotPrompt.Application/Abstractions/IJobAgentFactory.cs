using OneShotPrompt.Core.Models;

namespace OneShotPrompt.Application.Abstractions;

public interface IJobAgentFactory
{
    Task<PreparedJobAgent> CreateAsync(AppConfig config, JobDefinition job, string configDirectory, CancellationToken cancellationToken);
}