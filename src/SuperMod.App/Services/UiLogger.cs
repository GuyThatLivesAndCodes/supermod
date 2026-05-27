using Microsoft.Extensions.Logging;

namespace SuperMod.App.Services;

/// <summary>Receives formatted log lines and forwards them to subscribers (the UI).</summary>
public sealed class UiLogSink
{
    public event Action<LogLevel, string>? Emitted;
    public void Emit(LogLevel level, string message) => Emitted?.Invoke(level, message);
}

/// <summary>An <see cref="ILoggerProvider"/> that routes log entries to a <see cref="UiLogSink"/>.</summary>
public sealed class UiLoggerProvider : ILoggerProvider
{
    private readonly UiLogSink _sink;
    public UiLoggerProvider(UiLogSink sink) => _sink = sink;
    public ILogger CreateLogger(string categoryName) => new UiLogger(categoryName, _sink);
    public void Dispose() { }
}

internal sealed class UiLogger : ILogger
{
    private readonly string _category;
    private readonly UiLogSink _sink;

    public UiLogger(string category, UiLogSink sink)
    {
        _category = ShortCategory(category);
        _sink = sink;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var message = formatter(state, exception);
        if (exception is not null)
            message += " — " + exception.Message;

        _sink.Emit(logLevel, $"{_category}: {message}");
    }

    private static string ShortCategory(string category)
    {
        var lastDot = category.LastIndexOf('.');
        return lastDot >= 0 && lastDot < category.Length - 1 ? category[(lastDot + 1)..] : category;
    }
}
