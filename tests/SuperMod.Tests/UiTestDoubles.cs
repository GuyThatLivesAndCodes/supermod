using Microsoft.Extensions.Logging;
using SuperMod.App.Services;
using SuperMod.Configuration;
using SuperMod.Discord;

namespace SuperMod.Tests;

/// <summary>An <see cref="IBotController"/> that records calls and lets tests raise events.</summary>
internal sealed class FakeBotController : IBotController
{
    public BotStatus Status { get; set; } = BotStatus.Stopped;
    public SuperModOptions? StartedWith { get; private set; }
    public int StartCount { get; private set; }
    public int StopCount { get; private set; }
    public bool ThrowOnStart { get; set; }

    public event Action<BotStatus, string?>? StatusChanged;
    public event Action<ModerationActivity>? ActivityRecorded;
    public event Action<LogLevel, string>? LogEmitted;

    public Task StartAsync(SuperModOptions options, CancellationToken cancellationToken)
    {
        StartCount++;
        StartedWith = options;
        if (ThrowOnStart)
            throw new InvalidOperationException("start failed");
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        StopCount++;
        return Task.CompletedTask;
    }

    public void RaiseStatus(BotStatus status, string? message = null) => StatusChanged?.Invoke(status, message);
    public void RaiseActivity(ModerationActivity activity) => ActivityRecorded?.Invoke(activity);
    public void RaiseLog(LogLevel level, string message) => LogEmitted?.Invoke(level, message);
}

/// <summary>An in-memory <see cref="IConfigStore"/>.</summary>
internal sealed class FakeConfigStore : IConfigStore
{
    private SuperModOptions _stored;

    public FakeConfigStore(SuperModOptions? initial = null) => _stored = initial ?? new SuperModOptions();

    public SuperModOptions? Saved { get; private set; }
    public int SaveCount { get; private set; }
    public string Path => "/tmp/supermod/config.json";

    public SuperModOptions Load() => _stored;

    public void Save(SuperModOptions options)
    {
        Saved = options;
        _stored = options;
        SaveCount++;
    }
}
