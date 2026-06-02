using Marten.Events.Projections;
using TeleQ.Messaging.Shared.DomainEvents;

namespace TeleQ.Api.Common.Projections;

/// <summary>
/// Aggregated daily statistics per branch + service.
/// Keyed by "{yyyyMMdd}:{branchId}:{serviceId}".
/// Run as an async projection to avoid write-path overhead.
/// </summary>
public sealed class DailyQueueStats
{
    public string Id { get; set; } = null!;
    public DateOnly Date { get; set; }
    public Guid BranchId { get; set; }
    public Guid ServiceId { get; set; }
    public int TotalIssued { get; set; }
    public int TotalServed { get; set; }
    public int TotalNoShow { get; set; }
    public int TotalCancelled { get; set; }
    public int TotalAppointments { get; set; }
    public int TotalWalkIns { get; set; }
}

public sealed partial class DailyQueueStatsProjection : MultiStreamProjection<DailyQueueStats, string>
{
    public DailyQueueStatsProjection()
    {
        Identity<TicketIssued>(e => $"{e.IssuedAt:yyyyMMdd}:{e.BranchId}:{e.ServiceId}");
        Identity<AppointmentBooked>(e => $"{e.BookedAt:yyyyMMdd}:{e.BranchId}:{e.ServiceId}");
        Identity<TicketServed>(e => $"{e.ServedAt:yyyyMMdd}:{e.BranchId}:{e.ServiceId}");
        Identity<TicketNoShow>(e => $"{e.MarkedAt:yyyyMMdd}:{e.BranchId}:{e.ServiceId}");
        Identity<TicketCancelled>(e => $"{e.CancelledAt:yyyyMMdd}:{e.BranchId}:{e.ServiceId}");
    }

    public void Apply(TicketIssued e, DailyQueueStats doc)
    {
        doc.Date = DateOnly.FromDateTime(e.IssuedAt.Date);
        doc.BranchId = e.BranchId;
        doc.ServiceId = e.ServiceId;
        doc.TotalIssued++;
        doc.TotalWalkIns++;
    }

    public void Apply(AppointmentBooked e, DailyQueueStats doc)
    {
        doc.Date = DateOnly.FromDateTime(e.BookedAt.Date);
        doc.BranchId = e.BranchId;
        doc.ServiceId = e.ServiceId;
        doc.TotalIssued++;
        doc.TotalAppointments++;
    }

    public void Apply(TicketServed e, DailyQueueStats doc) => doc.TotalServed++;

    public void Apply(TicketNoShow e, DailyQueueStats doc) => doc.TotalNoShow++;

    public void Apply(TicketCancelled e, DailyQueueStats doc) => doc.TotalCancelled++;
}
