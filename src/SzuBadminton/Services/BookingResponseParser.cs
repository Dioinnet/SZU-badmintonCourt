using System.Text.Json;

namespace SzuBadminton.Services;

public sealed record BookingSubmissionResult(bool Success, string Message);

public static class BookingResponseParser
{
    public static BookingSubmissionResult Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var code = root.TryGetProperty("code", out var codeElement) ? codeElement.ToString() : string.Empty;
        var message = root.TryGetProperty("msg", out var messageElement) ? messageElement.ToString() : string.Empty;
        return new BookingSubmissionResult(code == "0", message);
    }
}
