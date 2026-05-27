using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SuperMod.Configuration;
using SuperMod.Moderation;

namespace SuperMod.Discord;

/// <summary>Performs moderation actions against Discord via the socket/REST client.</summary>
public sealed class DiscordModerationActions : IModerationActions
{
    private readonly DiscordSocketClient _client;
    private readonly ModerationOptions _options;
    private readonly ILogger<DiscordModerationActions> _log;

    public DiscordModerationActions(
        DiscordSocketClient client,
        IOptions<SuperModOptions> options,
        ILogger<DiscordModerationActions> log)
    {
        _client = client;
        _options = options.Value.Moderation;
        _log = log;
    }

    public async Task<string> TimeoutUsersAsync(
        ulong guildId,
        IReadOnlyCollection<ulong> userIds,
        TimeSpan duration,
        string reason,
        CancellationToken cancellationToken)
    {
        var guild = _client.GetGuild(guildId);
        if (guild is null)
            return "guild not available";

        var requestOptions = new RequestOptions { AuditLogReason = Trim(reason), CancelToken = cancellationToken };
        var done = new List<string>();

        foreach (var userId in userIds)
        {
            if (userId == _client.CurrentUser?.Id)
                continue;

            IGuildUser? member = guild.GetUser(userId);
            if (member is null)
            {
                try { member = await _client.Rest.GetGuildUserAsync(guildId, userId); }
                catch (Exception ex) { _log.LogDebug(ex, "Could not fetch user {UserId}.", userId); }
            }

            if (member is null)
            {
                _log.LogWarning("Skipping timeout: user {UserId} not found in guild {GuildId}.", userId, guildId);
                continue;
            }

            if (_options.ProtectModerators && IsProtected(guild, member))
            {
                _log.LogInformation("Skipping timeout of protected member {User} ({UserId}).", member.Username, userId);
                continue;
            }

            if (_options.DryRun)
            {
                done.Add($"[dry-run] timeout {member.Username} for {duration.TotalMinutes:0}m");
                continue;
            }

            try
            {
                await member.SetTimeOutAsync(duration, requestOptions);
                done.Add($"timed out {member.Username} for {duration.TotalMinutes:0}m");
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to time out {User} ({UserId}).", member.Username, userId);
            }
        }

        return done.Count > 0
            ? $"{string.Join("; ", done)} — reason: {Trim(reason)}"
            : "no users timed out";
    }

    public async Task<string> DeleteMessagesAsync(
        ulong guildId,
        ulong channelId,
        IReadOnlyCollection<ulong> messageIds,
        string reason,
        CancellationToken cancellationToken)
    {
        var channel = _client.GetGuild(guildId)?.GetTextChannel(channelId);
        if (channel is null)
            return "channel not available";

        var ids = messageIds.Distinct().ToArray();
        if (ids.Length == 0)
            return "no messages to delete";

        var requestOptions = new RequestOptions { AuditLogReason = Trim(reason), CancelToken = cancellationToken };

        if (_options.DryRun)
            return $"[dry-run] delete {ids.Length} message(s) — reason: {Trim(reason)}";

        // Bulk delete is fastest but only works for >=2 messages newer than 14 days.
        // Fall back to per-message deletes for a single id or when bulk fails.
        if (ids.Length >= 2)
        {
            try
            {
                await channel.DeleteMessagesAsync(ids, requestOptions);
                return $"deleted {ids.Length} messages — reason: {Trim(reason)}";
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Bulk delete failed; falling back to individual deletes.");
            }
        }

        var deleted = 0;
        foreach (var id in ids)
        {
            try
            {
                await channel.DeleteMessageAsync(id, requestOptions);
                deleted++;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to delete message {MessageId}.", id);
            }
        }

        return $"deleted {deleted}/{ids.Length} messages — reason: {Trim(reason)}";
    }

    private static bool IsProtected(SocketGuild guild, IGuildUser user)
        => user.Id == guild.OwnerId
           || user.GuildPermissions.Administrator
           || user.GuildPermissions.ManageMessages
           || user.GuildPermissions.ModerateMembers;

    private static string Trim(string reason)
    {
        reason = string.IsNullOrWhiteSpace(reason) ? "Violated server rules." : reason.Trim();
        return reason.Length <= 400 ? reason : reason[..400];
    }
}
