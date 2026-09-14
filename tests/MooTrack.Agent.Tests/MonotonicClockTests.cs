using MooTrack.Agent;

namespace MooTrack.Agent.Tests;

public class MonotonicClockTests
{
    [Fact]
    public void Milliseconds_ReturnsAPositiveReading()
    {
        Assert.True(MonotonicClock.Milliseconds() > 0);
    }

    [Fact]
    public void Milliseconds_DoesNotGoBackwards()
    {
        var first = MonotonicClock.Milliseconds();
        Thread.Sleep(10);
        var second = MonotonicClock.Milliseconds();

        Assert.True(second >= first, $"{second} < {first}");
    }

    // The Win32 counter is unavailable off Windows, and could in principle fail on it.
    // Losing an observation because a clock could not be read would be the wrong trade:
    // the timestamp is what the record needs, the monotonic reading corroborates it.
    [Fact]
    public void Milliseconds_NeverThrows()
    {
        var exception = Record.Exception(() => MonotonicClock.Milliseconds());

        Assert.Null(exception);
    }

    [Fact]
    public void Portable_IsUsableWhenTheWin32CounterIsNot()
    {
        Assert.True(MonotonicClock.Portable() > 0);
    }
}
