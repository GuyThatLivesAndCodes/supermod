using Microsoft.Extensions.Logging;
using SuperMod.Configuration;
using SuperMod.Discord;

namespace SuperMod.App.Services;

/// <summary>
/// UI-facing facade over the bot engine. Abstracted so the view-model can be
/// unit-tested with a fake controller.
/// </summary>
public interface IBotController
{
    BotStatus Status { get; }

    event Action<BotStatus, string?>? StatusChanged;
    event Action<ModerationActivity>? ActivityRecorded;
    event Action<LogLevel, string>? LogEmitted;

    Task StartAsync(SuperModOptions options, CancellationToken cancellationToken);
    Task StopAsync();
}
