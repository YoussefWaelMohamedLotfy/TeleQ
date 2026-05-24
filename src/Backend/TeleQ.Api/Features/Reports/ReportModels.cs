namespace TeleQ.Api.Features.Reports;

public sealed record TicketEventEntry(
    string EventType,
    object Data,
    DateTimeOffset Timestamp,
    long Version);
