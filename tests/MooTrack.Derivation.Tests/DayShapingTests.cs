using MooTrack.Derivation;

namespace MooTrack.Derivation.Tests;

public class DayShapingTests
{
    private static readonly TimeSpan Offset = TimeSpan.FromHours(10);

    private static DateTimeOffset At(int day, int hour, int minute = 0) =>
        new(2026, 8, day, hour, minute, 0, Offset);

    [Fact]
    public void SplitAtMidnight_IntervalCrossingMidnight_YieldsOnePerDay()
    {
        Interval[] active = [new(At(17, 22), At(18, 2))];

        var result = IntervalSet.SplitAtMidnight(active);

        Assert.Equal(
            [new Interval(At(17, 22), At(18, 0)), new Interval(At(18, 0), At(18, 2))],
            result);
    }

    [Fact]
    public void TrimFringe_IsolatedShortSessionLate_ExcludedFromCore()
    {
        Interval[] sessions = [new(At(17, 9), At(17, 17)), new(At(17, 23), At(17, 23).AddMinutes(3))];

        var core = DayShape.TrimFringe(
            sessions, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(90));

        Assert.Equal([new Interval(At(17, 9), At(17, 17))], core);
    }

    [Fact]
    public void TrimFringe_LongSessionAfterGap_KeptInCore()
    {
        Interval[] sessions = [new(At(17, 9), At(17, 12)), new(At(17, 16), At(17, 18))];

        var core = DayShape.TrimFringe(
            sessions, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(90));

        Assert.Equal(sessions, core);
    }
}
