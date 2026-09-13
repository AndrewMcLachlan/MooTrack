using System.Globalization;

namespace MooTrack.Derivation;

public static class CsvFormat
{
    public const string DailyHeader =
        "date,weekday,first_active,last_active,span_hours,active_hours,away_hours,"
        + "sessions,longest_break_min,fringe_sessions,quality,unaccounted_min";

    public const string WeeklyHeader =
        "iso_week,week_start,active_hours,days_with_activity,mean_hours_per_day,"
        + "total_span_hours,partial_days,unreliable_days";

    public static string Row(DayRecord day) => String.Join(',',
        day.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        day.Date.ToString("ddd", CultureInfo.InvariantCulture),
        Time(day.FirstActive),
        Time(day.LastActive),
        Hours(day.SpanHours),
        Hours(day.ActiveHours),
        Hours(day.AwayHours),
        day.Sessions.ToString(CultureInfo.InvariantCulture),
        day.LongestBreakMinutes.ToString(CultureInfo.InvariantCulture),
        day.FringeSessions.ToString(CultureInfo.InvariantCulture),
        day.Quality.ToString(),
        day.UnaccountedMinutes.ToString(CultureInfo.InvariantCulture));

    public static string Row(WeekRecord week) => String.Join(',',
        week.IsoWeek,
        week.WeekStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        Hours(week.ActiveHours),
        week.DaysWithActivity.ToString(CultureInfo.InvariantCulture),
        Hours(week.MeanHoursPerDay),
        Hours(week.TotalSpanHours),
        week.PartialDays.ToString(CultureInfo.InvariantCulture),
        week.UnreliableDays.ToString(CultureInfo.InvariantCulture));

    static string Time(TimeOnly? value) =>
        value?.ToString("HH:mm:ss", CultureInfo.InvariantCulture) ?? String.Empty;

    static string Hours(decimal value) =>
        value.ToString("0.00", CultureInfo.InvariantCulture);
}
