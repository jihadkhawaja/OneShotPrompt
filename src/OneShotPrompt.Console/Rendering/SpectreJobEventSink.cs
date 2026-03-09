using System.Diagnostics.CodeAnalysis;
using OneShotPrompt.Application.Abstractions;
using OneShotPrompt.Core.Models;
using Spectre.Console;

namespace OneShotPrompt.Console.Rendering;

[ExcludeFromCodeCoverage]
internal sealed class SpectreJobEventSink : IJobEventSink
{
    public void Emit(JobEvent jobEvent)
    {
        switch (jobEvent)
        {
            case ThinkingEvent thinking:
                if (!string.IsNullOrWhiteSpace(thinking.Text))
                {
                    AnsiConsole.MarkupLine($"  [grey]{Markup.Escape(thinking.Text)}[/]");
                }
                break;
            case ToolCallEvent toolCall:
                AnsiConsole.MarkupLine($"  [cyan]CALL {Markup.Escape(toolCall.ToolName)}[/] [dim]({Markup.Escape(Truncate(toolCall.Arguments, 200))})[/]");
                break;
            case ToolResultEvent toolResult:
                AnsiConsole.MarkupLine($"  [green]RESULT {Markup.Escape(toolResult.ToolName)}[/] [dim]{Markup.Escape(Truncate(toolResult.Result, 300))}[/]");
                break;
            case ResponseChunkEvent chunk:
                AnsiConsole.Write(chunk.Text);
                break;
            case JobLogEvent log:
                AnsiConsole.MarkupLine($"  [dim]{Markup.Escape(log.Message)}[/]");
                break;
        }
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }
}
