namespace TeleQ.Api.Features.Tickets;

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

public sealed record IssueWalkInTicketRequest(
    Guid BranchId,
    Guid ServiceId,
    string CustomerPhone);

public sealed record BookAppointmentRequest(
    Guid BranchId,
    Guid ServiceId,
    Guid TimeSlotId,
    string CustomerPhone);

public sealed record CancelTicketRequest(string CustomerPhone);

public sealed record RescheduleTicketRequest(string CustomerPhone, Guid NewTimeSlotId);
