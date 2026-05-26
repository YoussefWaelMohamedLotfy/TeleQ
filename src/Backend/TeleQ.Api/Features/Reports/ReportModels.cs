namespace TeleQ.Api.Features.Reports;

public sealed record TicketEventEntry(
    string EventType,
    object Data,
    DateTimeOffset Timestamp,
    long Version);

public sealed class DailyStatsRequest
{
    public Guid BranchId { get; set; }
    public Guid ServiceId { get; set; }
    public DateOnly? Date { get; set; }
}

public sealed class DailyStatsRangeRequest
{
    public Guid BranchId { get; set; }
    public Guid ServiceId { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
}
