using MooTrack.Derivation;

namespace MooTrack.Derivation.Tests;

public class IntervalSetTests
{
    static DateTimeOffset At(int hour, int minute = 0) =>
        new(2026, 8, 17, hour, minute, 0, TimeSpan.FromHours(10));

    [Fact]
    public void Subtract_AwayInsideActive_SplitsIntoTwo()
    {
        Interval[] active = [new(At(9), At(17))];
        Interval[] away = [new(At(12), At(13))];

        var result = IntervalSet.Subtract(active, away);

        Assert.Equal(
            [new Interval(At(9), At(12)), new Interval(At(13), At(17))],
            result);
    }
}

public class IntervalSetEdgeTests
{
    static DateTimeOffset At(int hour, int minute = 0) =>
        new(2026, 8, 17, hour, minute, 0, TimeSpan.FromHours(10));

    [Fact]
    public void Subtract_AwayCoversActive_ReturnsEmpty()
    {
        var result = IntervalSet.Subtract(
            [new Interval(At(9), At(17))], [new Interval(At(8), At(18))]);

        Assert.Empty(result);
    }

    [Fact]
    public void Subtract_AwayOverlapsStart_TruncatesFront()
    {
        var result = IntervalSet.Subtract(
            [new Interval(At(9), At(17))], [new Interval(At(8), At(10))]);

        Assert.Equal([new Interval(At(10), At(17))], result);
    }

    [Fact]
    public void Subtract_OverlappingAwayIntervals_TreatedAsOne()
    {
        var result = IntervalSet.Subtract(
            [new Interval(At(9), At(17))],
            [new Interval(At(12), At(13)), new Interval(At(12, 30), At(14))]);

        Assert.Equal(
            [new Interval(At(9), At(12)), new Interval(At(14), At(17))], result);
    }

    [Fact]
    public void Subtract_AwayOutsideActive_LeavesUnchanged()
    {
        var result = IntervalSet.Subtract(
            [new Interval(At(9), At(17))], [new Interval(At(18), At(19))]);

        Assert.Equal([new Interval(At(9), At(17))], result);
    }
}
