using Microsoft.Extensions.AI;
using OneShotPrompt.Application.Abstractions;
using OneShotPrompt.Core.Models;

namespace OneShotPrompt.Infrastructure.Providers;

internal sealed class ObservableChatClient(IChatClient innerClient, IJobEventSink eventSink) : DelegatingChatClient(innerClient)
{
    private readonly HashSet<string> _seenResultIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _callIdToToolName = [];

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var messages = chatMessages as IReadOnlyList<ChatMessage> ?? [.. chatMessages];
        EmitNewToolResults(messages);

        var response = await base.GetResponseAsync(messages, options, cancellationToken);

        foreach (var message in response.Messages)
        {
            foreach (var content in message.Contents)
            {
                if (content is TextReasoningContent reasoning)
                {
                    eventSink.Emit(new ThinkingEvent(reasoning.Text));
                }
                else if (content is FunctionCallContent functionCall)
                {
                    if (!string.IsNullOrEmpty(functionCall.CallId))
                    {
                        _callIdToToolName[functionCall.CallId] = functionCall.Name ?? "unknown";
                    }

                    eventSink.Emit(new ToolCallEvent(
                        functionCall.Name ?? "unknown",
                        FormatArguments(functionCall.Arguments)));
                }
            }
        }

        return response;
    }

    private void EmitNewToolResults(IReadOnlyList<ChatMessage> messages)
    {
        foreach (var message in messages)
        {
            foreach (var content in message.Contents)
            {
                if (content is FunctionResultContent resultContent)
                {
                    var id = resultContent.CallId;
                    if (id is not null && _seenResultIds.Add(id))
                    {
                        var toolName = _callIdToToolName.TryGetValue(id, out var name)
                            ? name
                            : "unknown";

                        eventSink.Emit(new ToolResultEvent(
                            toolName,
                            Truncate(resultContent.Result?.ToString() ?? string.Empty, 500)));
                    }
                }
            }
        }
    }

    private static string FormatArguments(IDictionary<string, object?>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(", ", arguments.Select(pair => $"{pair.Key}: {pair.Value}"));
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }
}
