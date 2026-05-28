using SuperMod.Discord;
using Xunit;

namespace SuperMod.Tests;

public class StartupBatchTests
{
    private static FetchedMessage Msg(ulong id, ulong authorId, string content, bool isBot = false) =>
        new(id, authorId, $"user{authorId}", isBot, content, DateTimeOffset.UnixEpoch.AddSeconds(id));

    [Fact]
    public void Reverses_newest_first_into_oldest_first()
    {
        // Discord returns newest-first; the transcript must read oldest-first.
        var fetched = new[] { Msg(3, 1, "third"), Msg(2, 1, "second"), Msg(1, 1, "first") };

        var batch = BotRunner.ToBatch(fetched, selfId: 999);

        Assert.Equal(new ulong[] { 1, 2, 3 }, batch.Select(m => m.MessageId));
        Assert.Equal("first", batch[0].Content);
        Assert.Equal("third", batch[2].Content);
    }

    [Fact]
    public void Drops_bots_self_and_empty_messages()
    {
        var fetched = new[]
        {
            Msg(5, 1, "keep me"),
            Msg(4, 2, "i am a bot", isBot: true),
            Msg(3, 999, "this is the bot itself"),
            Msg(2, 3, "   "),
            Msg(1, 4, "also keep")
        };

        var batch = BotRunner.ToBatch(fetched, selfId: 999);

        Assert.Equal(new ulong[] { 1, 5 }, batch.Select(m => m.MessageId));
    }

    [Fact]
    public void Empty_input_yields_empty_batch()
    {
        Assert.Empty(BotRunner.ToBatch(Array.Empty<FetchedMessage>(), selfId: 1));
    }
}
