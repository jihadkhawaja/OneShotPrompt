using OneShotPrompt.Core.Models;

namespace OneShotPrompt.Application.Abstractions;

public interface IAppConfigLoader
{
    Task<AppConfig> LoadAsync(string path, CancellationToken cancellationToken, ConfigLoadOptions? options = null);
}