using System.Diagnostics.CodeAnalysis;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using OneShotPrompt.Application.Abstractions;
using OneShotPrompt.Core.Models;

namespace OneShotPrompt.Infrastructure.Providers;

[ExcludeFromCodeCoverage]
internal sealed class CorporatePlanningJobAgent(Workflow workflow, IReadOnlyList<AIAgent> participants, IJobEventSink? eventSink) : IJobAgent, IAsyncDisposable, IDisposable
{
    public async Task<string> RunAsync(string prompt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        List<ChatMessage> messages = [new(ChatRole.User, prompt)];
        await using var run = await InProcessExecution.RunStreamingAsync(workflow, messages);
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

        List<ChatMessage>? conversationHistory = null;

        await foreach (var workflowEvent in run.WatchStreamAsync().WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (workflowEvent is AgentResponseUpdateEvent responseUpdate)
            {
                EmitGroupChatUpdate(responseUpdate);
                continue;
            }

            if (workflowEvent is WorkflowOutputEvent outputEvent)
            {
                conversationHistory = outputEvent.As<List<ChatMessage>>();
                break;
            }
        }

        if (conversationHistory is null || conversationHistory.Count == 0)
        {
            throw new InvalidOperationException("Corporate-planning workflow completed without a conversation history.");
        }

        return ExtractFinalResponse(conversationHistory);
    }

    private void EmitGroupChatUpdate(AgentResponseUpdateEvent responseUpdate)
    {
        if (eventSink is null)
        {
            return;
        }

        var update = responseUpdate.As<AgentResponseUpdate>();
        if (update is null)
        {
            return;
        }

        var agentName = string.IsNullOrWhiteSpace(update.AuthorName) ? responseUpdate.ExecutorId : update.AuthorName;

        foreach (var content in update.Contents)
        {
            if (content is not TextContent textContent)
            {
                continue;
            }

            var text = textContent.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            eventSink.Emit(new GroupChatMessageEvent(
                agentName,
                text));
        }
    }

    private static string ExtractFinalResponse(IReadOnlyList<ChatMessage> conversationHistory)
    {
        for (var index = conversationHistory.Count - 1; index >= 0; index--)
        {
            var text = conversationHistory[index].Text?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var finalResponseMarkerIndex = text.IndexOf("FINAL_RESPONSE:", StringComparison.OrdinalIgnoreCase);
            if (finalResponseMarkerIndex >= 0)
            {
                return text[(finalResponseMarkerIndex + "FINAL_RESPONSE:".Length)..].Trim();
            }

            var finalPlanMarkerIndex = text.IndexOf("FINAL_PLAN:", StringComparison.OrdinalIgnoreCase);
            if (finalPlanMarkerIndex >= 0)
            {
                return text[(finalPlanMarkerIndex + "FINAL_PLAN:".Length)..].Trim();
            }
        }

        for (var index = conversationHistory.Count - 1; index >= 0; index--)
        {
            var text = conversationHistory[index].Text?.Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return string.Empty;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var participant in participants)
        {
            switch (participant)
            {
                case IAsyncDisposable asyncDisposable:
                    await asyncDisposable.DisposeAsync();
                    break;
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }
        }
    }

    public void Dispose()
    {
        foreach (var participant in participants)
        {
            if (participant is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
