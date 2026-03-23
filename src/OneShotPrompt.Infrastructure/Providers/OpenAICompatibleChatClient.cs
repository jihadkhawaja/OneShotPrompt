using System.ClientModel;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace OneShotPrompt.Infrastructure.Providers;

internal sealed class OpenAICompatibleChatClient(IChatClient innerClient) : DelegatingChatClient(innerClient)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.GetResponseAsync(chatMessages, SanitizeOptions(options), cancellationToken);
        }
        catch (ClientResultException ex) when (ex.Status == 400)
        {
            throw new InvalidOperationException(BuildCompatibilityErrorMessage(ex.Message), ex);
        }
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        IAsyncEnumerator<ChatResponseUpdate> enumerator;

        try
        {
            enumerator = base.GetStreamingResponseAsync(chatMessages, SanitizeOptions(options), cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
        }
        catch (ClientResultException ex) when (ex.Status == 400)
        {
            throw new InvalidOperationException(BuildCompatibilityErrorMessage(ex.Message), ex);
        }

        await using (enumerator.ConfigureAwait(false))
        {
            while (true)
            {
                bool hasNext;

                try
                {
                    hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (ClientResultException ex) when (ex.Status == 400)
                {
                    throw new InvalidOperationException(BuildCompatibilityErrorMessage(ex.Message), ex);
                }

                if (!hasNext)
                {
                    yield break;
                }

                yield return enumerator.Current;
            }
        }
    }

    private static ChatOptions? SanitizeOptions(ChatOptions? options)
    {
        if (options is null)
        {
            return null;
        }

        var sanitized = options.Clone();
        sanitized.AdditionalProperties = [];
        sanitized.AllowMultipleToolCalls = null;
        sanitized.RawRepresentationFactory = null;
        sanitized.Reasoning = null;
        sanitized.ResponseFormat = null;
        sanitized.ToolMode = null!;
        return sanitized;
    }

    private static string BuildCompatibilityErrorMessage(string originalMessage)
    {
        return string.Join(
            Environment.NewLine,
            originalMessage,
            string.Empty,
            "OpenAI-compatible backends such as LM Studio accept a narrower chat-completions payload than the default Microsoft.Extensions.AI/OpenAI pipeline can emit.",
            "OneShotPrompt now strips advanced options such as reasoning, response format, extra provider properties, and tool-mode hints before sending OpenAI-compatible requests.",
            "If this 400 persists, verify that LM Studio is running, the target model is loaded, and the loaded model supports tool use for requests that include tools.");
    }
}