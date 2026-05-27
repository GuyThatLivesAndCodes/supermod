namespace SuperMod.Moderation;

/// <summary>
/// A thread-safe rolling buffer for a single channel.
///
/// It keeps the most recent <c>window</c> messages (default 20) and fires a
/// moderation pass every <c>step</c> new messages (default 10). Because the
/// window is twice the step, consecutive passes overlap by one step: the AI
/// always sees the 10 newest messages plus the 10 it saw last time, giving it
/// continuity of context.
/// </summary>
public sealed class ChannelBuffer
{
    private readonly int _window;
    private readonly int _step;
    private readonly LinkedList<BufferedMessage> _messages = new();
    private readonly object _gate = new();
    private int _sinceLastRun;

    public ChannelBuffer(int window, int step)
    {
        _window = Math.Max(1, window);
        _step = Math.Max(1, step);
    }

    /// <summary>
    /// Records a message. Returns a snapshot of the current window when a
    /// moderation pass is due, otherwise <c>null</c>.
    /// </summary>
    public IReadOnlyList<BufferedMessage>? Append(BufferedMessage message)
    {
        lock (_gate)
        {
            _messages.AddLast(message);
            while (_messages.Count > _window)
                _messages.RemoveFirst();

            _sinceLastRun++;
            if (_sinceLastRun < _step)
                return null;

            _sinceLastRun = 0;
            return _messages.ToArray();
        }
    }
}
