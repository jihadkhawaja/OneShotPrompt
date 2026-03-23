using Microsoft.Extensions.AI;

namespace OneShotPrompt.Tests;

public sealed class CorporatePlanningIntegrationTests
{
    [Fact]
    public void CorporatePlanningHelloWorld_ExtractsExactFinalResponsePayload()
    {
        var agentType = Type.GetType("OneShotPrompt.Infrastructure.Providers.CorporatePlanningJobAgent, OneShotPrompt.Infrastructure")!;
        IReadOnlyList<ChatMessage> conversationHistory =
        [
            new(ChatRole.User, "Say hello world."),
            new(ChatRole.Assistant, "We should keep the output minimal.")
            {
                AuthorName = "COORDINATOR",
            },
            new(ChatRole.Assistant, "FINAL_RESPONSE: Hello, world!")
            {
                AuthorName = "SYNTHESIZER",
            },
        ];

        var extracted = (string)ProcessTestHarness.InvokePrivateStatic(
            agentType,
            "ExtractFinalResponse",
            conversationHistory)!;

        Assert.Equal("Hello, world!", extracted);
    }

    [Fact]
    public void CorporatePlanning_StillAcceptsLegacyFinalPlanMarker()
    {
        var agentType = Type.GetType("OneShotPrompt.Infrastructure.Providers.CorporatePlanningJobAgent, OneShotPrompt.Infrastructure")!;
        IReadOnlyList<ChatMessage> conversationHistory =
        [
            new(ChatRole.User, "Create a plan."),
            new(ChatRole.Assistant, "FINAL_PLAN: 1. Do the thing.")
            {
                AuthorName = "SYNTHESIZER",
            },
        ];

        var extracted = (string)ProcessTestHarness.InvokePrivateStatic(
            agentType,
            "ExtractFinalResponse",
            conversationHistory)!;

        Assert.Equal("1. Do the thing.", extracted);
    }

    [Fact]
    public void CorporatePlanning_ExtractFinalResponse_PrefersLatestFinalResponsePayload()
    {
        var agentType = Type.GetType("OneShotPrompt.Infrastructure.Providers.CorporatePlanningJobAgent, OneShotPrompt.Infrastructure")!;
        IReadOnlyList<ChatMessage> conversationHistory =
        [
            new(ChatRole.User, "Return the exact payload."),
            new(ChatRole.Assistant, "FINAL_RESPONSE: Hello, world!")
            {
                AuthorName = "SYNTHESIZER",
            },
            new(ChatRole.Assistant, "FINAL_RESPONSE: Hello again")
            {
                AuthorName = "SYNTHESIZER",
            },
        ];

        var extracted = (string)ProcessTestHarness.InvokePrivateStatic(
            agentType,
            "ExtractFinalResponse",
            conversationHistory)!;

        Assert.Equal("Hello again", extracted);
    }
}