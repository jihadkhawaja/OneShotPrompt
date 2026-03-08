namespace OneShotPrompt.Core.Models;

public abstract record JobEvent;

public sealed record ThinkingEvent(string? Text = null) : JobEvent;

public sealed record ToolCallEvent(string ToolName, string Arguments) : JobEvent;

public sealed record ToolResultEvent(string ToolName, string Result) : JobEvent;

public sealed record ResponseChunkEvent(string Text) : JobEvent;

public sealed record JobLogEvent(string Message) : JobEvent;
