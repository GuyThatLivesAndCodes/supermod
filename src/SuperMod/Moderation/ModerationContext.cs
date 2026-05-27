namespace SuperMod.Moderation;

/// <summary>Everything the moderation pass needs about the channel under review.</summary>
public sealed record ModerationContext(
    ulong GuildId,
    ulong ChannelId,
    string ChannelName,
    IReadOnlyList<BufferedMessage> Messages);

/// <summary>Result of a moderation pass.</summary>
public sealed record ModerationOutcome(
    bool Success,
    bool ActionTaken,
    IReadOnlyList<string> Actions,
    string? Error)
{
    public static ModerationOutcome NoAction { get; } =
        new(true, false, Array.Empty<string>(), null);

    public static ModerationOutcome Failed(string error) =>
        new(false, false, Array.Empty<string>(), error);

    public static ModerationOutcome Acted(IReadOnlyList<string> actions) =>
        new(true, actions.Count > 0, actions, null);
}
