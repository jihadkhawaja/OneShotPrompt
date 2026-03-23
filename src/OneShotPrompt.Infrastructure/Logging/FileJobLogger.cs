using System.Text;
using OneShotPrompt.Application.Abstractions;
using OneShotPrompt.Core.Models;

namespace OneShotPrompt.Infrastructure.Logging;

public sealed class FileJobLogger : IJobEventSink, IAsyncDisposable
{
    private readonly StreamWriter _writer;

    public FileJobLogger(string logDirectory)
    {
        Directory.CreateDirectory(logDirectory);
        var fileName = $"oneshotprompt-{DateTime.UtcNow:yyyy-MM-dd-HHmmss}.log";
        _writer = new StreamWriter(Path.Combine(logDirectory, fileName), append: false, Encoding.UTF8)
        {
            AutoFlush = true,
        };
    }

    public void Emit(JobEvent jobEvent)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("O");
        var line = jobEvent switch
        {
            ThinkingEvent t => $"[{timestamp}] THINKING: {t.Text}",
            ToolCallEvent tc => $"[{timestamp}] TOOL_CALL: {tc.ToolName} | {tc.Arguments}",
            ToolResultEvent tr => $"[{timestamp}] TOOL_RESULT: {tr.ToolName} | {Truncate(tr.Result, 2000)}",
            ResponseChunkEvent rc => $"[{timestamp}] RESPONSE_CHUNK: {Truncate(rc.Text, 2000)}",
            GroupChatMessageEvent gc => $"[{timestamp}] GROUP_CHAT: {gc.AgentName} | {Truncate(gc.Text, 2000)}",
            OutputBoundaryEvent => $"[{timestamp}] OUTPUT_BOUNDARY",
            JobLogEvent log => $"[{timestamp}] LOG: {log.Message}",
            _ => $"[{timestamp}] EVENT: {jobEvent}",
        };

        _writer.WriteLine(line);
    }

    public async ValueTask DisposeAsync()
    {
        await _writer.DisposeAsync();
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }
}
