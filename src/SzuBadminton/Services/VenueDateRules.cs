namespace SzuBadminton.Services;

public static class VenueDateRules
{
    public static IReadOnlyList<string> GetExpectedDates(DateTime now)
    {
        var firstDate = now.TimeOfDay < new TimeSpan(22, 0, 0) ? now.Date : now.Date.AddDays(1);
        return
        [
            firstDate.ToString("yyyy-MM-dd"),
            firstDate.AddDays(1).ToString("yyyy-MM-dd")
        ];
    }

    public static string GetDefaultTargetDate(DateTime now) =>
        now.TimeOfDay < BookingSchedule.ReleaseTime
            ? now.Date.ToString("yyyy-MM-dd")
            : now.Date.AddDays(1).ToString("yyyy-MM-dd");
}
