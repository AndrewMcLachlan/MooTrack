namespace MooTrack.Agent;

/// <summary>
/// A clock that does not move when the wall clock is corrected, used to corroborate
/// recorded timestamps. 81 time-change events were observed in a fortnight on the
/// target machine, so a duration computed from wall clock alone cannot be trusted.
/// </summary>
public static class MonotonicClock
{
    /// <summary>
    /// Never throws. The timestamp is what an observation needs; this reading only
    /// corroborates it, so failing to read it must not cost the observation.
    /// </summary>
    public static long Milliseconds()
    {
        try
        {
            if (OperatingSystem.IsWindows()
                && Native.QueryUnbiasedInterruptTime(out var unbiased))
            {
                return (long)(unbiased / 10_000);
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
        }

        return Portable();
    }

    /// <summary>
    /// Counts time the machine spent suspended, which the Win32 counter deliberately
    /// does not. Only reached where that counter is unavailable.
    /// </summary>
    internal static long Portable() => Environment.TickCount64;
}
