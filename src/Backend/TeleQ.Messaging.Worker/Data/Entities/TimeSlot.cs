namespace TeleQ.Messaging.Worker.Data.Entities;

/// <summary>
/// Defines a bookable appointment slot for a service at a branch.
/// </summary>
public sealed class TimeSlot
{
    public Guid Id { get; set; }
    public Guid ServiceId { get; set; }
    public Guid BranchId { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public int Capacity { get; set; } = 1;
    public int BookedCount { get; set; }
    public bool IsRecurring { get; set; }
    public DayOfWeek? DayOfWeek { get; set; }
    public DateOnly? Date { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }

    public Service Service { get; set; } = null!;
}
