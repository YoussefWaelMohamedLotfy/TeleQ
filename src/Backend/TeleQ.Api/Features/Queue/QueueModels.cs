namespace TeleQ.Api.Features.Queue;

public sealed record QueueResponse(
    Guid BranchId,
    Guid ServiceId,
    List<QueueEntryResponse> WaitingTickets,
    List<QueueEntryResponse> CalledTickets,
    int TotalServedToday,
    int TotalNoShowToday,
    int TotalCancelledToday,
    int EstimatedWaitMinutes);

public sealed record QueueEntryResponse(
    Guid TicketId,
    string TicketNumber,
    string CustomerPhone,
    int QueuePosition,
    DateTimeOffset IssuedAt,
    DateTimeOffset? ScheduledAt,
    string Type,
    int EstimatedWaitMinutes);

public sealed record MyPositionResponse(
    Guid TicketId,
    string TicketNumber,
    string Status,
    int QueuePosition,
    int AheadCount,
    int EstimatedWaitMinutes);

public sealed record CallNextRequest(Guid BranchId, Guid ServiceId);

public sealed record CallNextResponse(
    Guid TicketId,
    string TicketNumber,
    string CustomerPhone,
    string CounterLabel,
    DateTimeOffset CalledAt);
