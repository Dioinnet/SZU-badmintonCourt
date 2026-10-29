using System.Text.Json;

namespace SzuBadminton.Services;

public sealed record DirectBookingResult(bool Success, string Message, string Court = "", bool AlreadyBooked = false)
{
    public static DirectBookingResult Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var success = root.TryGetProperty("success", out var ok) && ok.GetBoolean();
        var alreadyBooked = root.TryGetProperty("alreadyBooked", out var duplicate) && duplicate.GetBoolean();
        var message = root.TryGetProperty("message", out var msg) ? msg.GetString() ?? string.Empty : string.Empty;
        var court = root.TryGetProperty("court", out var courtElement) ? courtElement.GetString() ?? string.Empty : string.Empty;
        return new DirectBookingResult(success || alreadyBooked, message, court, alreadyBooked);
    }
}
