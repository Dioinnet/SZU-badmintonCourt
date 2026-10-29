namespace SzuBadminton.Services;

public sealed class BookingSlotTracker
{
    private readonly List<string> _pending;

    public BookingSlotTracker(IEnumerable<string> timeSlots)
    {
        _pending = timeSlots.Where(time => !string.IsNullOrWhiteSpace(time)).Distinct().Take(2).ToList();
        if (_pending.Count == 0)
        {
            throw new ArgumentException("至少需要一个候选时段。", nameof(timeSlots));
        }
    }

    public IReadOnlyList<string> Pending => _pending;
    public bool AllBooked => _pending.Count == 0;

    public void MarkBooked(string timeSlot)
    {
        if (!_pending.Remove(timeSlot))
        {
            throw new InvalidOperationException($"时段 {timeSlot} 已完成或不存在。");
        }
    }
}
