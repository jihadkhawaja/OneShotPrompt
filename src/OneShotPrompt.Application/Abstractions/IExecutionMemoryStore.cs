using OneShotPrompt.Core.Models;

namespace OneShotPrompt.Application.Abstractions;

public interface IExecutionMemoryStore
{
    Task<JobMemoryDocument> LoadAsync(string configDirectory, string jobName, CancellationToken cancellationToken);

    Task SaveAsync(string configDirectory, string jobName, JobMemoryDocument document, CancellationToken cancellationToken);
}