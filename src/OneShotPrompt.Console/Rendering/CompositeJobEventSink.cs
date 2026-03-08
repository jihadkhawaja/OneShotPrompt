using OneShotPrompt.Application.Abstractions;
using OneShotPrompt.Core.Models;

namespace OneShotPrompt.Console.Rendering;

internal sealed class CompositeJobEventSink(IJobEventSink[] sinks) : IJobEventSink, IAsyncDisposable
{
    public void Emit(JobEvent jobEvent)
    {
        foreach (var sink in sinks)
        {
            sink.Emit(jobEvent);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var sink in sinks)
        {
            if (sink is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else if (sink is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
