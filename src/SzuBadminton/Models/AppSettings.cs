namespace SzuBadminton.Models;

public sealed class AppSettings
{
    public List<SavedAccount> Accounts { get; set; } = [];
    public string SelectedAccountId { get; set; } = string.Empty;
    public bool AutoLogin { get; set; } = true;
    public bool AutoOpenBadminton { get; set; } = true;
    public bool DailyAutoBooking { get; set; } = true;
    public ReleaseBookingTask? ScheduledReleaseBooking { get; set; }

    public string Campus { get; set; } = "粤海校区";
    public string PreferredTime { get; set; } = "19:00-20:00";
    public string SecondaryPreferredTime { get; set; } = "20:00-21:00";
    public string CourtPreference { get; set; } = string.Empty;
    public int RefreshSeconds { get; set; } = 3;
    public int ScheduleWindowMinutes { get; set; } = 10;

    public bool UseBrowserProxy { get; set; }
    public string ProxyScheme { get; set; } = "http";
    public string ProxyHost { get; set; } = "127.0.0.1";
    public int ProxyPort { get; set; } = 8080;

    public string ProxyExecutablePath { get; set; } = string.Empty;
    public string ProxyArguments { get; set; } = string.Empty;
    public bool StopProxyOnExit { get; set; } = true;
}
