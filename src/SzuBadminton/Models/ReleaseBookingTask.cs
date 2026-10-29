namespace SzuBadminton.Models;

public sealed class ReleaseBookingTask
{
    public DateTime RunAt { get; set; }
    public string TargetDate { get; set; } = string.Empty;
    public string Campus { get; set; } = string.Empty;
    public string PreferredTime { get; set; } = string.Empty;
    public string SecondaryPreferredTime { get; set; } = string.Empty;
    public string CourtPreference { get; set; } = string.Empty;
    public int RefreshSeconds { get; set; } = 3;
}
