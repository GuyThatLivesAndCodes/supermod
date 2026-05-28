namespace SuperMod.Moderation;

/// <summary>
/// Outcome of a single moderation action.
/// <paramref name="Summary"/> is a human-readable line for the activity feed;
/// <paramref name="AffectedIds"/> are the ids actually acted on (timed-out user
/// ids for a timeout, deleted message ids for a delete) so callers can notify
/// exactly the right people.
/// </summary>
public sealed record ModerationActionResult(string Summary, IReadOnlyList<ulong> AffectedIds)
{
    public static ModerationActionResult None(string summary) => new(summary, Array.Empty<ulong>());
}

/// <summary>
/// The side-effecting moderation operations the AI can invoke. Implemented for
/// real by the Discord layer and faked in tests, which keeps the moderation
/// pipeline fully testable without a live gateway connection.
/// </summary>
public interface IModerationActions
{
    /// <summary>Times out the given users. Returns the summary and the ids actually timed out.</summary>
    Task<ModerationActionResult> TimeoutUsersAsync(
        ulong guildId,
        IReadOnlyCollection<ulong> userIds,
        TimeSpan duration,
        string reason,
        CancellationToken cancellationToken);

    /// <summary>Deletes the given messages. Returns the summary and the message ids actually deleted.</summary>
    Task<ModerationActionResult> DeleteMessagesAsync(
        ulong guildId,
        ulong channelId,
        IReadOnlyCollection<ulong> messageIds,
        string reason,
        CancellationToken cancellationToken);

    /// <summary>Sends each user a private DM (the moderation notice). Best-effort.</summary>
    Task NotifyUsersAsync(
        ulong guildId,
        IReadOnlyCollection<ulong> userIds,
        string message,
        CancellationToken cancellationToken);
}
