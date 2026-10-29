namespace SzuBadminton.Services;

public sealed record BookingSelectionResult(bool Success, string Message, string Court = "")
{
    public static BookingSelectionResult Failure(string message) => new(false, message);
    public static BookingSelectionResult Selected(string court) => new(true, "已选中可用场地。", court);
}
