using OneShotPrompt.Core.Models;

namespace OneShotPrompt.Application.Abstractions;

public interface IJobAgentFactory
{
    Task<IJobAgent> CreateAsync(AppConfig config, JobDefinition job, CancellationToken cancellationToken);
}