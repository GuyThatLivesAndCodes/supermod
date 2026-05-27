using System.Collections.Concurrent;
using System.Net.Http.Headers;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SuperMod.Ai;
using SuperMod.Configuration;
using SuperMod.Moderation;

namespace SuperMod.Discord;

public enum BotStatus
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Faulted
}

/// <summary>A single moderation action, surfaced to the UI's live feed.</summary>
public sealed record ModerationActivity(DateTimeOffset Timestamp, string Channel, string Detail);

/// <summary>
/// Owns the Discord gateway connection and the moderation pipeline for one bot
/// session. Unlike a hosted service it can be started and stopped on demand and
/// raises events the UI can subscribe to. All dependencies are built from the
/// supplied options and logger factory, so no DI container is required.
/// </summary>
public sealed class BotRunner : IAsyncDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<BotRunner> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<ulong, byte> _running = new();

    private DiscordSocketClient? _client;
    private HttpClient? _http;
    private ModerationService? _moderation;
    private MessageBufferStore? _buffers;

    public BotRunner(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _log = loggerFactory.CreateLogger<BotRunner>();
    }

    public BotStatus Status { get; private set; } = BotStatus.Stopped;

    public event Action<BotStatus, string?>? StatusChanged;
    public event Action<ModerationActivity>? ActivityRecorded;

    public async Task StartAsync(SuperModOptions options)
    {
        await _gate.WaitAsync();
        try
        {
            if (Status is BotStatus.Running or BotStatus.Starting)
                return;

            options.Validate();
            SetStatus(BotStatus.Starting);

            var optionsWrapper = Options.Create(options);

            var baseUrl = options.Ai.ResolveBaseUrl();
            if (!baseUrl.EndsWith('/'))
                baseUrl += "/";

            _http = new HttpClient
            {
                BaseAddress = new Uri(baseUrl),
                Timeout = TimeSpan.FromSeconds(Math.Max(10, options.Ai.RequestTimeoutSeconds))
            };
            if (!string.IsNullOrWhiteSpace(options.Ai.ApiKey))
                _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.Ai.ApiKey);

            var chatClient = new OpenAiChatClient(_http, optionsWrapper, _loggerFactory.CreateLogger<OpenAiChatClient>());

            _client = new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.MessageContent,
                MessageCacheSize = 0,
                LogLevel = LogSeverity.Info,
                AlwaysDownloadUsers = false
            });

            var actions = new DiscordModerationActions(_client, optionsWrapper, _loggerFactory.CreateLogger<DiscordModerationActions>());
            _moderation = new ModerationService(chatClient, actions, optionsWrapper, _loggerFactory.CreateLogger<ModerationService>());
            _buffers = new MessageBufferStore(optionsWrapper);

            _client.Log += OnLog;
            _client.Ready += OnReady;
            _client.MessageReceived += OnMessageReceived;
            _client.Disconnected += OnDisconnected;

            await _client.LoginAsync(TokenType.Bot, options.DiscordToken);
            await _client.StartAsync();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to start the bot.");
            await CleanUpAsync();
            SetStatus(BotStatus.Faulted, ex.Message);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (Status is BotStatus.Stopped or BotStatus.Stopping)
                return;

            SetStatus(BotStatus.Stopping);
            await CleanUpAsync();
            SetStatus(BotStatus.Stopped);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task CleanUpAsync()
    {
        if (_client is not null)
        {
            _client.Log -= OnLog;
            _client.Ready -= OnReady;
            _client.MessageReceived -= OnMessageReceived;
            _client.Disconnected -= OnDisconnected;

            try
            {
                await _client.StopAsync();
                await _client.LogoutAsync();
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Error while stopping the Discord client.");
            }

            await _client.DisposeAsync();
            _client = null;
        }

        _http?.Dispose();
        _http = null;
        _moderation = null;
        _buffers = null;
        _running.Clear();
    }

    private Task OnReady()
    {
        _log.LogInformation("Logged in as {User} in {Guilds} guild(s).",
            _client?.CurrentUser?.Username, _client?.Guilds.Count);
        SetStatus(BotStatus.Running);
        return Task.CompletedTask;
    }

    private Task OnDisconnected(Exception ex)
    {
        if (ex is not null)
            _log.LogWarning(ex, "Disconnected from Discord; the client will attempt to reconnect.");
        return Task.CompletedTask;
    }

    private Task OnMessageReceived(SocketMessage rawMessage)
    {
        if (_buffers is null || _client is null)
            return Task.CompletedTask;
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
            message.Id, message.Author.Id, DisplayName(message.Author), content, message.Timestamp);

        var batch = _buffers.Append(channel.Id, buffered);
        if (batch is not null)
            _ = RunModerationAsync(channel, batch);

        return Task.CompletedTask;
    }

    private async Task RunModerationAsync(SocketTextChannel channel, IReadOnlyList<BufferedMessage> batch)
    {
        if (_moderation is null || !_running.TryAdd(channel.Id, 0))
            return;

        try
        {
            var context = new ModerationContext(channel.Guild.Id, channel.Id, channel.Name, batch);
            var outcome = await _moderation.ModerateAsync(context, CancellationToken.None);

            if (outcome.ActionTaken)
            {
                foreach (var action in outcome.Actions)
                    ActivityRecorded?.Invoke(new ModerationActivity(DateTimeOffset.Now, channel.Name, action));
            }
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

    private void SetStatus(BotStatus status, string? message = null)
    {
        Status = status;
        StatusChanged?.Invoke(status, message);
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

    public async ValueTask DisposeAsync()
    {
        await CleanUpAsync();
        _gate.Dispose();
    }
}
