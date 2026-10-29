namespace SzuBadminton.Services;

public static class BookingSchedule
{
    public static readonly TimeSpan ReleaseTime = new(12, 30, 0);
    public static readonly TimeSpan BookingStartTime = ReleaseTime.Add(TimeSpan.FromMinutes(-1));

    public static DateTime GetNextBookingStart(DateTime now)
    {
        var todayStart = now.Date + BookingStartTime;
        return now <= todayStart ? todayStart : todayStart.AddDays(1);
    }

    public static DateTime GetNextRun(DateTime now)
    {
        var todayRelease = now.Date + ReleaseTime;
        return now <= todayRelease ? todayRelease : todayRelease.AddDays(1);
    }

    public static bool IsWithinRunWindow(DateTime now, DateTime scheduledAt, int windowMinutes) =>
        now >= scheduledAt && now < scheduledAt.AddMinutes(windowMinutes);

    public static DateTime GetReleaseAt(string targetDate) =>
        DateTime.Parse(targetDate).Date.AddDays(-1) + ReleaseTime;

    public static bool IsTargetReleased(DateTime now, string targetDate) =>
        now >= GetReleaseAt(targetDate);

    public static string GetTargetDate(DateTime now) => now.Date.AddDays(1).ToString("yyyy-MM-dd");
}
