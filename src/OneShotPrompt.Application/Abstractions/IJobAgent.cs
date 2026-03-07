namespace OneShotPrompt.Application.Abstractions;

public interface IJobAgent
{
    Task<string> RunAsync(string prompt, CancellationToken cancellationToken);
}