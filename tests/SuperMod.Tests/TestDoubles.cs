using System.Text.Json;
using SuperMod.Ai;
using SuperMod.Moderation;

namespace SuperMod.Tests;

/// <summary>An <see cref="IChatClient"/> that returns a canned reply and records inputs.</summary>
internal sealed class FakeChatClient : IChatClient
{
    private readonly ChatMessage _reply;
    private readonly Exception? _throw;

    public FakeChatClient(ChatMessage reply) => _reply = reply;
    public FakeChatClient(Exception toThrow) { _throw = toThrow; _reply = new ChatMessage(); }

    public IReadOnlyList<ChatMessage>? LastMessages { get; private set; }
    public IReadOnlyList<ChatTool>? LastTools { get; private set; }

    public Task<ChatMessage> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ChatTool> tools,
        CancellationToken cancellationToken)
    {
        LastMessages = messages;
        LastTools = tools;
        if (_throw is not null)
            throw _throw;
        return Task.FromResult(_reply);
    }
}

internal sealed record TimeoutCall(ulong GuildId, IReadOnlyCollection<ulong> UserIds, TimeSpan Duration, string Reason);
internal sealed record DeleteCall(ulong GuildId, ulong ChannelId, IReadOnlyCollection<ulong> MessageIds, string Reason);

/// <summary>An <see cref="IModerationActions"/> that records every invocation.</summary>
internal sealed class RecordingActions : IModerationActions
{
    public List<TimeoutCall> Timeouts { get; } = new();
    public List<DeleteCall> Deletes { get; } = new();

    public Task<string> TimeoutUsersAsync(ulong guildId, IReadOnlyCollection<ulong> userIds, TimeSpan duration, string reason, CancellationToken cancellationToken)
    {
        Timeouts.Add(new TimeoutCall(guildId, userIds, duration, reason));
        return Task.FromResult($"timed out {userIds.Count} user(s)");
    }

    public Task<string> DeleteMessagesAsync(ulong guildId, ulong channelId, IReadOnlyCollection<ulong> messageIds, string reason, CancellationToken cancellationToken)
    {
        Deletes.Add(new DeleteCall(guildId, channelId, messageIds, reason));
        return Task.FromResult($"deleted {messageIds.Count} message(s)");
    }
}

internal static class ToolCallFactory
{
    /// <summary>Builds a tool call whose arguments are a raw JSON object (some backends do this).</summary>
    public static ToolCall FromObject(string name, object args) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Function = new ToolCallFunction
        {
            Name = name,
            Arguments = JsonSerializer.SerializeToElement(args)
        }
    };

    /// <summary>Builds a tool call whose arguments are a JSON-encoded string (the OpenAI spec).</summary>
    public static ToolCall FromJsonString(string name, string argsJson) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Function = new ToolCallFunction
        {
            Name = name,
            Arguments = JsonSerializer.SerializeToElement(argsJson)
        }
    };
}
