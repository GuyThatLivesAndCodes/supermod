namespace SuperMod.Ai;

/// <summary>Abstraction over an OpenAI-compatible chat completion endpoint.</summary>
public interface IChatClient
{
    /// <summary>
    /// Sends the conversation plus tool definitions and returns the assistant's
    /// reply message (which may contain content, tool calls, or both).
    /// </summary>
    Task<ChatMessage> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ChatTool> tools,
        CancellationToken cancellationToken);
}
