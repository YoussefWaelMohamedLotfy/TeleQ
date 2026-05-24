namespace TeleQ.Api.Data.Entities;

/// <summary>
/// Defines a bookable appointment slot for a service at a branch.
/// Recurring slots are generated per DayOfWeek within the branch's operating hours.
/// </summary>
public sealed class TimeSlot
{
    public Guid Id { get; set; }
    public Guid ServiceId { get; set; }
    public Guid BranchId { get; set; }

    /// <summary>Start time of the slot (time-of-day portion).</summary>
    public TimeOnly StartTime { get; set; }

    /// <summary>End time of the slot (time-of-day portion).</summary>
    public TimeOnly EndTime { get; set; }

    /// <summary>Maximum number of appointments that can be booked in this slot.</summary>
    public int Capacity { get; set; } = 1;

    /// <summary>Current number of confirmed bookings in this slot.</summary>
    public int BookedCount { get; set; }

    /// <summary>Whether this slot repeats weekly on <see cref="DayOfWeek"/>.</summary>
    public bool IsRecurring { get; set; }

    /// <summary>Day of week for recurring slots (null = one-off slot on <see cref="Date"/>).</summary>
    public DayOfWeek? DayOfWeek { get; set; }

    /// <summary>Specific date for one-off slots.</summary>
    public DateOnly? Date { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }

    public Service Service { get; set; } = null!;
}
