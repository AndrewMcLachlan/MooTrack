using MooTrack.Derivation;

namespace MooTrack.Derivation.Tests;

public class BridgeTests
{
    static DateTimeOffset At(int hour, int minute = 0) =>
        new(2026, 8, 17, hour, minute, 0, TimeSpan.FromHours(10));

    [Fact]
    public void Bridge_GapBelowThreshold_MergesIntoOneBlock()
    {
        Interval[] active = [new(At(9), At(10)), new(At(10, 8), At(11))];

        var result = IntervalSet.Bridge(active, TimeSpan.FromMinutes(10));

        Assert.Equal([new Interval(At(9), At(11))], result);
    }

    [Fact]
    public void Bridge_GapAboveThreshold_LeavesBlocksSeparate()
    {
        Interval[] active = [new(At(9), At(10)), new(At(10, 12), At(11))];

        var result = IntervalSet.Bridge(active, TimeSpan.FromMinutes(10));

        Assert.Equal(
            [new Interval(At(9), At(10)), new Interval(At(10, 12), At(11))],
            result);
    }
}
