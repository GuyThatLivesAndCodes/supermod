namespace SuperMod.Moderation;

/// <summary>A snapshot of a Discord message captured for moderation review.</summary>
public sealed record BufferedMessage(
    ulong MessageId,
    ulong AuthorId,
    string AuthorName,
    string Content,
    DateTimeOffset Timestamp);
