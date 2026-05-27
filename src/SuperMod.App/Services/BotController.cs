using Microsoft.Extensions.Logging;
using SuperMod.Configuration;
using SuperMod.Discord;

namespace SuperMod.App.Services;

/// <summary>
/// Wraps a <see cref="BotRunner"/> and a logger factory whose output is piped to
/// the UI. Forwards the runner's status/activity events and the log stream.
/// </summary>
public sealed class BotController : IBotController, IAsyncDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly BotRunner _runner;

    public BotController()
    {
        var sink = new UiLogSink();
        sink.Emitted += (level, message) => LogEmitted?.Invoke(level, message);

        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddProvider(new UiLoggerProvider(sink));
        });

        _runner = new BotRunner(_loggerFactory);
        _runner.StatusChanged += (status, message) => StatusChanged?.Invoke(status, message);
        _runner.ActivityRecorded += activity => ActivityRecorded?.Invoke(activity);
    }

    public BotStatus Status => _runner.Status;

    public event Action<BotStatus, string?>? StatusChanged;
    public event Action<ModerationActivity>? ActivityRecorded;
    public event Action<LogLevel, string>? LogEmitted;

    public Task StartAsync(SuperModOptions options, CancellationToken cancellationToken) => _runner.StartAsync(options);

    public Task StopAsync() => _runner.StopAsync();

    public async ValueTask DisposeAsync()
    {
        await _runner.DisposeAsync();
        _loggerFactory.Dispose();
    }
}
