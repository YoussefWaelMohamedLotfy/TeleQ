using FastEndpoints;
using TeleQ.Api.Common.Aggregates;

namespace TeleQ.Api.Features.Tickets;

/// <summary>Maps from a <see cref="Ticket"/> aggregate to <see cref="TicketResponse"/>. Tickets are created via domain events, so only <c>FromEntity</c> mapping is provided.</summary>
public sealed class TicketMapper : ResponseMapper<TicketResponse, Ticket>
{
    public override TicketResponse FromEntity(Ticket t) =>
        new(t.Id, t.TicketNumber, t.Type.ToString(), t.Status.ToString(),
            t.CustomerPhone, t.BranchId, t.ServiceId, t.TimeSlotId,
            t.ScheduledAt, t.QueuePosition, t.CounterLabel, t.IssuedAt);
}
