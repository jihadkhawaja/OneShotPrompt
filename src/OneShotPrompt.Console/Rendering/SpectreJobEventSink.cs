using System.Diagnostics.CodeAnalysis;
using OneShotPrompt.Application.Abstractions;
using OneShotPrompt.Core.Models;
using Spectre.Console;

namespace OneShotPrompt.Console.Rendering;

[ExcludeFromCodeCoverage]
internal sealed class SpectreJobEventSink : IJobEventSink
{
    private string? _activeGroupChatAgent;
    private bool _groupChatLineOpen;

    public void Emit(JobEvent jobEvent)
    {
        switch (jobEvent)
        {
            case ThinkingEvent thinking:
                FlushGroupChatLine();
                if (!string.IsNullOrWhiteSpace(thinking.Text))
                {
                    AnsiConsole.MarkupLine($"  [grey]{Markup.Escape(thinking.Text)}[/]");
                }
                break;
            case ToolCallEvent toolCall:
                FlushGroupChatLine();
                AnsiConsole.MarkupLine($"  [cyan]CALL {Markup.Escape(toolCall.ToolName)}[/] [dim]({Markup.Escape(Truncate(toolCall.Arguments, 200))})[/]");
                break;
            case ToolResultEvent toolResult:
                FlushGroupChatLine();
                AnsiConsole.MarkupLine($"  [green]RESULT {Markup.Escape(toolResult.ToolName)}[/] [dim]{Markup.Escape(Truncate(toolResult.Result, 300))}[/]");
                break;
            case ResponseChunkEvent chunk:
                FlushGroupChatLine();
                AnsiConsole.Write(chunk.Text);
                break;
            case GroupChatMessageEvent groupChat:
                WriteGroupChatChunk(groupChat);
                break;
            case OutputBoundaryEvent:
                FlushGroupChatLine();
                break;
            case JobLogEvent log:
                FlushGroupChatLine();
                AnsiConsole.MarkupLine($"  [dim]{Markup.Escape(log.Message)}[/]");
                break;
        }
    }

    private void WriteGroupChatChunk(GroupChatMessageEvent groupChat)
    {
        if (!_groupChatLineOpen || !string.Equals(_activeGroupChatAgent, groupChat.AgentName, StringComparison.Ordinal))
        {
            FlushGroupChatLine();
            AnsiConsole.Markup($"  [yellow]{Markup.Escape($"[{groupChat.AgentName}]")}[/]: ");
            _activeGroupChatAgent = groupChat.AgentName;
            _groupChatLineOpen = true;
        }

        AnsiConsole.Markup(Markup.Escape(Truncate(groupChat.Text, 400)));
    }

    private void FlushGroupChatLine()
    {
        if (!_groupChatLineOpen)
        {
            return;
        }

        AnsiConsole.WriteLine();
        _groupChatLineOpen = false;
        _activeGroupChatAgent = null;
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }
}
