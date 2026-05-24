using FastEndpoints;
using FastEndpoints.AspVersioning;
using Marten;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Hybrid;
using TeleQ.Api.Common;
using TeleQ.Api.Common.Aggregates;
using TeleQ.Api.Common.DomainEvents;
using TeleQ.Api.Common.Projections;
using TeleQ.Api.Data;
using TeleQ.Api.Features.Notifications;

namespace TeleQ.Api.Features.Tickets;

/// <summary>Books an appointment ticket for a future time slot.</summary>
public sealed class BookAppointmentEndpoint(
    AppDbContext db,
    IDocumentSession session,
    IHubContext<QueueHub> hub,
    TicketMapper mapper,
    HybridCache cache) : Endpoint<BookAppointmentRequest, TicketResponse>
{
    public override void Configure()
    {
        Post("/tickets/appointment");
        Version(1);
        Description(x => x.WithTags("Tickets"));
        Options(x => x.WithVersionSet("TeleQ").MapToApiVersion(1.0));
    }

    public override async Task HandleAsync(BookAppointmentRequest req, CancellationToken ct)
    {
        var slot = await db.TimeSlots.FindAsync([req.TimeSlotId], ct);

        if (slot is null || !slot.IsActive || slot.ServiceId != req.ServiceId || slot.BranchId != req.BranchId)
        {
            AddError("Time slot not found or does not belong to the specified service/branch.");
            await Send.ErrorsAsync(404, ct);
            return;
        }

        if (slot.BookedCount >= slot.Capacity)
        {
            AddError("This time slot is fully booked.");
            await Send.ErrorsAsync(409, ct);
            return;
        }

        var scheduledAt = SlotScheduler.NextOccurrence(slot);

        if (scheduledAt <= DateTimeOffset.UtcNow)
        {
            AddError("Cannot book a slot in the past.");
            await Send.ErrorsAsync(400, ct);
            return;
        }

        var queueId = $"{req.BranchId}:{req.ServiceId}";
        var queue = await session.LoadAsync<BranchQueueSnapshot>(queueId, ct);
        var queuePosition = (queue?.NextQueueNumber ?? 1);
        var ticketNumber = $"B-{queuePosition:D3}";

        var ticketId = Guid.NewGuid();
        var evt = new AppointmentBooked(
            TicketId: ticketId,
            TicketNumber: ticketNumber,
            CustomerPhone: req.CustomerPhone,
            BranchId: req.BranchId,
            ServiceId: req.ServiceId,
            TimeSlotId: req.TimeSlotId,
            ScheduledAt: scheduledAt,
            QueuePosition: queuePosition,
            BookedAt: DateTimeOffset.UtcNow);

        session.Events.StartStream<Ticket>(ticketId, evt);

        slot.BookedCount++;

        await db.SaveChangesAsync(ct);
        await session.SaveChangesAsync(ct);
        await cache.RemoveByTagAsync([$"queue:{req.BranchId}:{req.ServiceId}", $"timeslot:{req.TimeSlotId}", "timeslots"], ct);

        var ticket = await session.Events.AggregateStreamAsync<Ticket>(ticketId, token: ct);

        await hub.Clients.Group(QueueHub.GroupName(req.BranchId, req.ServiceId))
            .SendAsync("QueueUpdated", new { TicketNumber = ticketNumber, QueuePosition = queuePosition }, ct);

        await Send.CreatedAtAsync<GetTicketEndpoint>(
            new { id = ticketId },
            mapper.FromEntity(ticket!),
            cancellation: ct);
    }
}
