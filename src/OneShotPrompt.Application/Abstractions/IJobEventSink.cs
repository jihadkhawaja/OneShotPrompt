using OneShotPrompt.Core.Models;

namespace OneShotPrompt.Application.Abstractions;

public interface IJobEventSink
{
    void Emit(JobEvent jobEvent);
}
