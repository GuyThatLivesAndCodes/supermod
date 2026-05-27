using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using SuperMod.Configuration;

namespace SuperMod.Moderation;

/// <summary>Holds one <see cref="ChannelBuffer"/> per channel.</summary>
public sealed class MessageBufferStore
{
    private readonly ConcurrentDictionary<ulong, ChannelBuffer> _buffers = new();
    private readonly int _window;
    private readonly int _step;

    public MessageBufferStore(IOptions<SuperModOptions> options)
    {
        var moderation = options.Value.Moderation;
        _window = moderation.ContextWindow;
        _step = moderation.MessagesPerBatch;
    }

    /// <summary>
    /// Records a message for its channel and returns the batch to moderate when
    /// one is due, otherwise <c>null</c>.
    /// </summary>
    public IReadOnlyList<BufferedMessage>? Append(ulong channelId, BufferedMessage message)
        => _buffers.GetOrAdd(channelId, _ => new ChannelBuffer(_window, _step)).Append(message);
}
