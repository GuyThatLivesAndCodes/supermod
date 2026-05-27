using SuperMod.Moderation;
using Xunit;

namespace SuperMod.Tests;

public class ChannelBufferTests
{
    private static BufferedMessage Msg(ulong id) =>
        new(id, 1000 + id, $"user{id}", $"message {id}", DateTimeOffset.UnixEpoch.AddSeconds(id));

    [Fact]
    public void Does_not_trigger_before_step_is_reached()
    {
        var buffer = new ChannelBuffer(window: 20, step: 10);
        for (ulong i = 1; i <= 9; i++)
            Assert.Null(buffer.Append(Msg(i)));
    }

    [Fact]
    public void Triggers_every_step_messages()
    {
        var buffer = new ChannelBuffer(window: 20, step: 10);
        var triggerSizes = new List<int>();

        for (ulong i = 1; i <= 30; i++)
        {
            var batch = buffer.Append(Msg(i));
            if (batch is not null)
                triggerSizes.Add(batch.Count);
        }

        // Triggers at message 10, 20 and 30.
        Assert.Equal(new[] { 10, 20, 20 }, triggerSizes);
    }

    [Fact]
    public void Window_overlaps_previous_batch_by_one_step()
    {
        // The defining behaviour: window 20, step 10 => each pass shares its
        // older half with the previous pass.
        var buffer = new ChannelBuffer(window: 20, step: 10);

        IReadOnlyList<BufferedMessage>? batchAt20 = null;
        IReadOnlyList<BufferedMessage>? batchAt30 = null;

        for (ulong i = 1; i <= 30; i++)
        {
            var batch = buffer.Append(Msg(i));
            if (i == 20) batchAt20 = batch;
            if (i == 30) batchAt30 = batch;
        }

        Assert.NotNull(batchAt20);
        Assert.NotNull(batchAt30);

        // Pass at message 20 covers ids 1..20.
        Assert.Equal(Enumerable.Range(1, 20).Select(x => (ulong)x), batchAt20!.Select(m => m.MessageId));
        // Pass at message 30 covers ids 11..30.
        Assert.Equal(Enumerable.Range(11, 20).Select(x => (ulong)x), batchAt30!.Select(m => m.MessageId));

        // The overlap is exactly the 10 messages 11..20.
        var overlap = batchAt20!.Select(m => m.MessageId)
            .Intersect(batchAt30!.Select(m => m.MessageId))
            .OrderBy(x => x);
        Assert.Equal(Enumerable.Range(11, 10).Select(x => (ulong)x), overlap);
    }

    [Fact]
    public void Never_exceeds_window_size()
    {
        var buffer = new ChannelBuffer(window: 20, step: 10);
        for (ulong i = 1; i <= 100; i++)
        {
            var batch = buffer.Append(Msg(i));
            if (batch is not null)
                Assert.True(batch.Count <= 20);
        }
    }

    [Fact]
    public void Respects_custom_step_and_window()
    {
        var buffer = new ChannelBuffer(window: 5, step: 2);
        var triggers = new List<int>();
        for (ulong i = 1; i <= 6; i++)
        {
            var batch = buffer.Append(Msg(i));
            if (batch is not null)
                triggers.Add(batch.Count);
        }

        // Triggers at 2, 4, 6 messages; window caps at 5.
        Assert.Equal(new[] { 2, 4, 5 }, triggers);
    }
}
