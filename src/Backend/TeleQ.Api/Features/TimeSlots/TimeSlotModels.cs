namespace TeleQ.Api.Features.TimeSlots;

public sealed record TimeSlotResponse(
    Guid Id,
    Guid ServiceId,
    Guid BranchId,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int Capacity,
    int BookedCount,
    int AvailableCount,
    bool IsRecurring,
    DayOfWeek? DayOfWeek,
    DateOnly? Date,
    bool IsActive);

public sealed record CreateTimeSlotRequest(
    Guid BranchId,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int Capacity,
    bool IsRecurring,
    DayOfWeek? DayOfWeek,
    DateOnly? Date);
