namespace TeleQ.Web.Models;

public sealed record BranchResponse(
    Guid Id,
    string Name,
    string Address,
    string? PhoneNumber,
    bool IsActive,
    DateTimeOffset? CreatedAt = null);

public sealed record CreateBranchRequest(string Name, string Address, string? PhoneNumber);
public sealed record UpdateBranchRequest(string Name, string Address, string? PhoneNumber, bool IsActive);

public sealed record ServiceResponse(
    Guid Id,
    string Name,
    string? Description,
    int EstimatedDurationMinutes,
    Guid BranchId,
    bool IsActive,
    DateTimeOffset? CreatedAt = null);

public sealed record CreateServiceRequest(string Name, string? Description, int EstimatedDurationMinutes);
public sealed record UpdateServiceRequest(string Name, string? Description, int EstimatedDurationMinutes, bool IsActive);

public sealed record TimeSlotResponse(
    Guid Id,
    Guid ServiceId,
    Guid BranchId,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int Capacity,
    int BookedCount,
    bool IsActive,
    bool IsRecurring,
    DayOfWeek? DayOfWeek,
    DateOnly? Date = null,
    int AvailableCount = 0);

public sealed record CreateTimeSlotRequest(
    TimeOnly StartTime,
    TimeOnly EndTime,
    int Capacity,
    bool IsRecurring,
    DayOfWeek? DayOfWeek,
    Guid? BranchId = null,
    DateOnly? Date = null);

public sealed record TicketResponse(
    Guid Id,
    string TicketNumber,
    string Type,
    string Status,
    string CustomerPhone,
    Guid BranchId,
    Guid ServiceId,
    Guid? TimeSlotId,
    DateTimeOffset? ScheduledAt,
    int QueuePosition,
    string? CounterLabel,
    DateTimeOffset IssuedAt);

public sealed record IssueWalkInRequest(Guid BranchId, Guid ServiceId, string CustomerPhone);
public sealed record BookAppointmentRequest(Guid BranchId, Guid ServiceId, Guid TimeSlotId, string CustomerPhone);
public sealed record CancelTicketRequest(string CustomerPhone);
public sealed record RescheduleTicketRequest(string CustomerPhone, Guid NewTimeSlotId);

public sealed record QueueStateResponse(
    int WaitingCount,
    int CalledCount,
    int NextQueueNumber,
    IReadOnlyList<QueueTicketItem> WaitingTickets,
    IReadOnlyList<QueueTicketItem> CalledTickets,
    int EstimatedWaitMinutes,
    int TotalServedToday = 0,
    int TotalNoShowToday = 0,
    int TotalCancelledToday = 0);

public sealed record QueueTicketItem(
    Guid TicketId,
    string TicketNumber,
    string CustomerPhone,
    int QueuePosition,
    string? CounterLabel,
    DateTimeOffset? ScheduledAt = null,
    string? Type = null,
    int EstimatedWaitMinutes = 0);

public sealed record CallNextRequest(Guid BranchId, Guid ServiceId, Guid ClerkId, string CounterLabel);
public sealed record ServeTicketRequest(Guid ClerkId);
public sealed record NoShowRequest(Guid? ClerkId);

public sealed record TicketEventRecord(string EventType, DateTimeOffset Timestamp, object Data);

public sealed record DailyStatsResponse(
    int TotalIssued,
    int TotalServed,
    int TotalNoShow,
    int TotalCancelled,
    double AverageWaitMinutes,
    int TotalAppointments = 0,
    int TotalWalkIns = 0);

public sealed record MyPositionResponse(
    Guid TicketId,
    string TicketNumber,
    string Status,
    int QueuePosition,
    int AheadCount,
    int EstimatedWaitMinutes);
