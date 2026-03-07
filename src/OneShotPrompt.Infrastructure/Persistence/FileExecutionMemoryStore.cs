using System.Text.Json;
using OneShotPrompt.Application.Abstractions;
using OneShotPrompt.Core.Models;

namespace OneShotPrompt.Infrastructure.Persistence;

public sealed class FileExecutionMemoryStore : IExecutionMemoryStore
{
    public async Task<JobMemoryDocument> LoadAsync(string configDirectory, string jobName, CancellationToken cancellationToken)
    {
        var path = GetFilePath(configDirectory, jobName);

        if (!File.Exists(path))
        {
            return new JobMemoryDocument();
        }

        await using var stream = File.OpenRead(path);
        var document = await JsonSerializer.DeserializeAsync(stream, JobMemoryJsonContext.Default.JobMemoryDocument, cancellationToken);
        return document ?? new JobMemoryDocument();
    }

    public async Task SaveAsync(string configDirectory, string jobName, JobMemoryDocument document, CancellationToken cancellationToken)
    {
        var path = GetFilePath(configDirectory, jobName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, document, JobMemoryJsonContext.Default.JobMemoryDocument, cancellationToken);
    }

    private static string GetFilePath(string configDirectory, string jobName)
    {
        var safeName = string.Concat(jobName.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        return Path.Combine(configDirectory, ".oneshotprompt", "memory", safeName + ".json");
    }
}