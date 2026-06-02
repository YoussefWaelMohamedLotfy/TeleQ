using FastEndpoints;
using FastEndpoints.AspVersioning;
using Marten;
using Microsoft.Extensions.Caching.Hybrid;
using TeleQ.Api.Common;
using TeleQ.Messaging.Shared.Aggregates;
using TeleQ.Messaging.Shared.DomainEvents;
using TeleQ.Api.Data;

namespace TeleQ.Api.Features.Tickets;

/// <summary>Reschedules an appointment ticket to a new time slot. The customer must provide their phone number to confirm ownership.</summary>
public sealed class RescheduleTicketEndpoint(
    AppDbContext db,
    IDocumentSession session,
    HybridCache cache) : Endpoint<RescheduleTicketRequest>
{
    public override void Configure()
    {
        Patch("/tickets/{id:guid}/reschedule");
        Version(1);
        Description(x => x.WithTags("Tickets"));
        Options(x => x.WithVersionSet("TeleQ").MapToApiVersion(1.0));
    }

    public override async Task HandleAsync(RescheduleTicketRequest req, CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var ticket = await session.Events.AggregateStreamAsync<Ticket>(id, token: ct);

        if (ticket is null) { await Send.NotFoundAsync(ct); return; }

        if (ticket.CustomerPhone != req.CustomerPhone)
        {
            await Send.ForbiddenAsync(ct);
            return;
        }

        if (ticket.Type != TicketType.Appointment)
        {
            AddError("Only appointment tickets can be rescheduled.");
            await Send.ErrorsAsync(400, ct);
            return;
        }

        if (ticket.Status is not TicketStatus.Waiting)
        {
            AddError($"Cannot reschedule a ticket with status '{ticket.Status}'.");
            await Send.ErrorsAsync(409, ct);
            return;
        }

        var newSlot = await db.TimeSlots.FindAsync([req.NewTimeSlotId], ct);
        if (newSlot is null || !newSlot.IsActive)
        {
            AddError("New time slot not found or inactive.");
            await Send.ErrorsAsync(404, ct);
            return;
        }

        if (newSlot.BookedCount >= newSlot.Capacity)
        {
            AddError("The new time slot is fully booked.");
            await Send.ErrorsAsync(409, ct);
            return;
        }

        if (ticket.TimeSlotId.HasValue)
        {
            var oldSlot = await db.TimeSlots.FindAsync([ticket.TimeSlotId.Value], ct);
            if (oldSlot is not null)
                oldSlot.BookedCount = Math.Max(0, oldSlot.BookedCount - 1);
        }

        newSlot.BookedCount++;

        var scheduledAt = SlotScheduler.NextOccurrence(newSlot);

        var evt = new TicketRescheduled(
            TicketId: id,
            BranchId: ticket.BranchId,
            ServiceId: ticket.ServiceId,
            OldTimeSlotId: ticket.TimeSlotId ?? req.NewTimeSlotId,
            NewTimeSlotId: req.NewTimeSlotId,
            NewScheduledAt: scheduledAt,
            RescheduledAt: DateTimeOffset.UtcNow);

        session.Events.Append(id, evt);
        await db.SaveChangesAsync(ct);
        await session.SaveChangesAsync(ct);

        var tagsToInvalidate = new List<string> { $"ticket:{id}", $"timeslot:{req.NewTimeSlotId}", "timeslots" };
        if (ticket.TimeSlotId.HasValue)
            tagsToInvalidate.Add($"timeslot:{ticket.TimeSlotId.Value}");
        await cache.RemoveByTagAsync(tagsToInvalidate, ct);

        await Send.NoContentAsync(ct);
    }
}
