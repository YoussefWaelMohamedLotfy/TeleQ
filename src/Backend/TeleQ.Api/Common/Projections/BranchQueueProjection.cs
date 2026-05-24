using Marten.Events.Projections;
using TeleQ.Api.Common.Aggregates;
using TeleQ.Api.Common.DomainEvents;

namespace TeleQ.Api.Common.Projections;

/// <summary>
/// Read model entry for a single ticket in a queue.
/// </summary>
public sealed class QueueEntry
{
    public Guid TicketId { get; set; }
    public string TicketNumber { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public int QueuePosition { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset? ScheduledAt { get; set; }
    public TicketType Type { get; set; }
}

/// <summary>
/// Live queue state for a specific branch + service combination.
/// Updated inline whenever a ticket event is appended.
/// </summary>
public sealed class BranchQueueSnapshot
{
    public string Id { get; set; } = null!; // "{branchId}:{serviceId}"
    public Guid BranchId { get; set; }
    public Guid ServiceId { get; set; }
    public List<QueueEntry> WaitingTickets { get; set; } = [];
    public List<QueueEntry> CalledTickets { get; set; } = [];
    public int TotalServedToday { get; set; }
    public int TotalNoShowToday { get; set; }
    public int TotalCancelledToday { get; set; }
    public int NextQueueNumber { get; set; } = 1;
}

/// <summary>
/// Marten multi-stream projection that builds BranchQueueSnapshot
/// by aggregating ticket events across all ticket streams for a branch/service.
/// </summary>
public sealed partial class BranchQueueProjection : MultiStreamProjection<BranchQueueSnapshot, string>
{
    public BranchQueueProjection()
    {
        Identity<TicketIssued>(e => $"{e.BranchId}:{e.ServiceId}");
        Identity<AppointmentBooked>(e => $"{e.BranchId}:{e.ServiceId}");
        Identity<TicketCalled>(e => $"{e.BranchId}:{e.ServiceId}");
        Identity<TicketServed>(e => $"{e.BranchId}:{e.ServiceId}");
        Identity<TicketNoShow>(e => $"{e.BranchId}:{e.ServiceId}");
        Identity<TicketCancelled>(e => $"{e.BranchId}:{e.ServiceId}");
        Identity<TicketRescheduled>(e => $"{e.BranchId}:{e.ServiceId}");
    }

    public void Apply(TicketIssued e, BranchQueueSnapshot doc)
    {
        doc.BranchId = e.BranchId;
        doc.ServiceId = e.ServiceId;
        doc.WaitingTickets.Add(new QueueEntry
        {
            TicketId = e.TicketId,
            TicketNumber = e.TicketNumber,
            CustomerPhone = e.CustomerPhone,
            QueuePosition = e.QueuePosition,
            IssuedAt = e.IssuedAt,
            Type = TicketType.WalkIn
        });
        doc.NextQueueNumber = Math.Max(doc.NextQueueNumber, e.QueuePosition + 1);
    }

    public void Apply(AppointmentBooked e, BranchQueueSnapshot doc)
    {
        doc.BranchId = e.BranchId;
        doc.ServiceId = e.ServiceId;
        doc.WaitingTickets.Add(new QueueEntry
        {
            TicketId = e.TicketId,
            TicketNumber = e.TicketNumber,
            CustomerPhone = e.CustomerPhone,
            QueuePosition = e.QueuePosition,
            IssuedAt = e.BookedAt,
            ScheduledAt = e.ScheduledAt,
            Type = TicketType.Appointment
        });
        doc.NextQueueNumber = Math.Max(doc.NextQueueNumber, e.QueuePosition + 1);
    }

    public void Apply(TicketCalled e, BranchQueueSnapshot doc)
    {
        var entry = doc.WaitingTickets.FirstOrDefault(t => t.TicketId == e.TicketId);
        if (entry is not null)
        {
            doc.WaitingTickets.Remove(entry);
            doc.CalledTickets.Add(entry);
        }
    }

    public void Apply(TicketServed e, BranchQueueSnapshot doc)
    {
        doc.CalledTickets.RemoveAll(t => t.TicketId == e.TicketId);
        doc.TotalServedToday++;
    }

    public void Apply(TicketNoShow e, BranchQueueSnapshot doc)
    {
        doc.CalledTickets.RemoveAll(t => t.TicketId == e.TicketId);
        doc.WaitingTickets.RemoveAll(t => t.TicketId == e.TicketId);
        doc.TotalNoShowToday++;
    }

    public void Apply(TicketCancelled e, BranchQueueSnapshot doc)
    {
        doc.WaitingTickets.RemoveAll(t => t.TicketId == e.TicketId);
        doc.CalledTickets.RemoveAll(t => t.TicketId == e.TicketId);
        doc.TotalCancelledToday++;
    }

    public void Apply(TicketRescheduled e, BranchQueueSnapshot doc)
    {
        var entry = doc.WaitingTickets.FirstOrDefault(t => t.TicketId == e.TicketId);
        entry?.ScheduledAt = e.NewScheduledAt;
    }
}
