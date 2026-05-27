using System.Collections.Concurrent;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SuperMod.Configuration;
using SuperMod.Moderation;

namespace SuperMod.Discord;

/// <summary>
/// Hosts the Discord gateway connection: buffers incoming messages and kicks
/// off a moderation pass whenever a channel's batch threshold is reached.
/// </summary>
public sealed class DiscordBotService : BackgroundService
{
    private readonly DiscordSocketClient _client;
    private readonly MessageBufferStore _buffers;
    private readonly ModerationService _moderation;
    private readonly SuperModOptions _options;
    private readonly ILogger<DiscordBotService> _log;

    // Channels with a moderation pass currently in flight; prevents overlapping
    // runs (and request pile-ups) on the same channel.
    private readonly ConcurrentDictionary<ulong, byte> _running = new();

    public DiscordBotService(
        DiscordSocketClient client,
        MessageBufferStore buffers,
        ModerationService moderation,
        IOptions<SuperModOptions> options,
        ILogger<DiscordBotService> log)
    {
        _client = client;
        _buffers = buffers;
        _moderation = moderation;
        _options = options.Value;
        _log = log;

        _client.Log += OnLog;
        _client.Ready += OnReady;
        _client.MessageReceived += OnMessageReceived;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _client.LoginAsync(TokenType.Bot, _options.DiscordToken);
        await _client.StartAsync();
        _log.LogInformation(
            "SuperMod connected. AI provider={Provider}, model={Model}, batch every {Step} msgs, window {Window}, dryRun={DryRun}.",
            _options.Ai.Provider, _options.Ai.Model, _options.Moderation.MessagesPerBatch,
            _options.Moderation.ContextWindow, _options.Moderation.DryRun);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        finally
        {
            await _client.StopAsync();
            await _client.LogoutAsync();
        }
    }

    private Task OnReady()
    {
        _log.LogInformation("Logged in as {User} in {Guilds} guild(s).",
            _client.CurrentUser?.Username, _client.Guilds.Count);
        return Task.CompletedTask;
    }

    private Task OnMessageReceived(SocketMessage rawMessage)
    {
        // Ignore non-user messages, bots (incl. ourselves) and non-text-channel messages.
        if (rawMessage is not SocketUserMessage message)
            return Task.CompletedTask;
        if (message.Author.IsBot || message.Author.Id == _client.CurrentUser?.Id)
            return Task.CompletedTask;
        if (message.Channel is not SocketTextChannel channel)
            return Task.CompletedTask;

        var content = ExtractContent(message);
        if (string.IsNullOrWhiteSpace(content))
            return Task.CompletedTask;

        var buffered = new BufferedMessage(
            message.Id,
            message.Author.Id,
            DisplayName(message.Author),
            content,
            message.Timestamp);

        var batch = _buffers.Append(channel.Id, buffered);
        if (batch is not null)
            _ = RunModerationAsync(channel, batch);

        return Task.CompletedTask;
    }

    private async Task RunModerationAsync(SocketTextChannel channel, IReadOnlyList<BufferedMessage> batch)
    {
        // Skip if a pass is already running for this channel.
        if (!_running.TryAdd(channel.Id, 0))
            return;

        try
        {
            var context = new ModerationContext(channel.Guild.Id, channel.Id, channel.Name, batch);
            await _moderation.ModerateAsync(context, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Moderation pass failed for #{Channel}.", channel.Name);
        }
        finally
        {
            _running.TryRemove(channel.Id, out _);
        }
    }

    private static string ExtractContent(SocketUserMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.Content))
            return message.Content;

        if (message.Attachments.Count > 0)
            return "[attachments: " + string.Join(", ", message.Attachments.Select(a => a.Filename)) + "]";

        return string.Empty;
    }

    private static string DisplayName(SocketUser user)
        => (user as SocketGuildUser)?.Nickname ?? user.GlobalName ?? user.Username;

    private Task OnLog(LogMessage entry)
    {
        var level = entry.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Debug,
            LogSeverity.Debug => LogLevel.Trace,
            _ => LogLevel.Information
        };
        _log.Log(level, entry.Exception, "[Discord:{Source}] {Message}", entry.Source, entry.Message);
        return Task.CompletedTask;
    }
}
