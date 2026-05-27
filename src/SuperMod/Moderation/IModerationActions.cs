namespace SuperMod.Moderation;

/// <summary>
/// The side-effecting moderation operations the AI can invoke. Implemented for
/// real by the Discord layer and faked in tests, which keeps the moderation
/// pipeline fully testable without a live gateway connection.
/// </summary>
public interface IModerationActions
{
    /// <summary>Times out the given users for the given duration. Returns a human-readable result summary.</summary>
    Task<string> TimeoutUsersAsync(
        ulong guildId,
        IReadOnlyCollection<ulong> userIds,
        TimeSpan duration,
        string reason,
        CancellationToken cancellationToken);

    /// <summary>Deletes the given messages from the channel. Returns a human-readable result summary.</summary>
    Task<string> DeleteMessagesAsync(
        ulong guildId,
        ulong channelId,
        IReadOnlyCollection<ulong> messageIds,
        string reason,
        CancellationToken cancellationToken);
}
