using FastEndpoints;
using FastEndpoints.AspVersioning;
using Marten;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using TeleQ.Api.Common.Aggregates;
using TeleQ.Api.Common.DomainEvents;
using TeleQ.Api.Common.Projections;
using TeleQ.Api.Data;
using TeleQ.Api.Features.Notifications;

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

/// <summary>Issues a walk-in ticket, placing the customer in the queue immediately.</summary>
public sealed class IssueWalkInTicketEndpoint(
    AppDbContext db,
    IDocumentSession session,
    IHubContext<QueueHub> hub,
    TicketMapper mapper) : Endpoint<IssueWalkInTicketRequest, TicketResponse>
{
    public override void Configure()
    {
        Post("/tickets/walkin");
        Version(1);
        AllowAnonymous();
        Description(x => x.WithTags("Tickets"));
        Options(x => x.WithVersionSet("TeleQ").MapToApiVersion(1.0));
    }

    public override async Task HandleAsync(IssueWalkInTicketRequest req, CancellationToken ct)
    {
        if (!await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .AnyAsync(db.Services, s => s.Id == req.ServiceId && s.BranchId == req.BranchId && s.IsActive, ct))
        {
            AddError("Service not found or inactive for the given branch.");
            await Send.ErrorsAsync(404, ct);
            return;
        }

        var queueId = $"{req.BranchId}:{req.ServiceId}";
        var queue = await session.LoadAsync<BranchQueueSnapshot>(queueId, ct);
        var queuePosition = (queue?.NextQueueNumber ?? 1);
        var ticketNumber = $"A-{queuePosition:D3}";

        var ticketId = Guid.NewGuid();
        var evt = new TicketIssued(
            TicketId: ticketId,
            TicketNumber: ticketNumber,
            CustomerPhone: req.CustomerPhone,
            BranchId: req.BranchId,
            ServiceId: req.ServiceId,
            QueuePosition: queuePosition,
            IssuedAt: DateTimeOffset.UtcNow);

        session.Events.StartStream<Ticket>(ticketId, evt);
        await session.SaveChangesAsync(ct);

        var ticket = await session.Events.AggregateStreamAsync<Ticket>(ticketId, token: ct);

        await hub.Clients.Group(QueueHub.GroupName(req.BranchId, req.ServiceId))
            .SendAsync("QueueUpdated", new { TicketNumber = ticketNumber, QueuePosition = queuePosition }, ct);

        await Send.CreatedAtAsync<GetTicketEndpoint>(
            new { id = ticketId },
            mapper.FromEntity(ticket!),
            cancellation: ct);
    }
}

public sealed record BookAppointmentRequest(
    Guid BranchId,
    Guid ServiceId,
    Guid TimeSlotId,
    string CustomerPhone);

/// <summary>Books an appointment ticket for a future time slot.</summary>
public sealed class BookAppointmentEndpoint(
    AppDbContext db,
    IDocumentSession session,
    IHubContext<QueueHub> hub,
    TicketMapper mapper) : Endpoint<BookAppointmentRequest, TicketResponse>
{
    public override void Configure()
    {
        Post("/tickets/appointment");
        Version(1);
        AllowAnonymous();
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

        var ticket = await session.Events.AggregateStreamAsync<Ticket>(ticketId, token: ct);

        await hub.Clients.Group(QueueHub.GroupName(req.BranchId, req.ServiceId))
            .SendAsync("QueueUpdated", new { TicketNumber = ticketNumber, QueuePosition = queuePosition }, ct);

        await Send.CreatedAtAsync<GetTicketEndpoint>(
            new { id = ticketId },
            mapper.FromEntity(ticket!),
            cancellation: ct);
    }
}

/// <summary>Returns a ticket's current state by replaying its event stream.</summary>
public sealed class GetTicketEndpoint(IDocumentSession session)
    : EndpointWithoutRequest<TicketResponse, TicketMapper>
{
    public override void Configure()
    {
        Get("/tickets/{id:guid}");
        Version(1);
        AllowAnonymous();
        Description(x => x.WithTags("Tickets"));
        Options(x => x.WithVersionSet("TeleQ").MapToApiVersion(1.0));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var ticket = await session.Events.AggregateStreamAsync<Ticket>(id, token: ct);

        if (ticket is null) { await Send.NotFoundAsync(ct); return; }

        await Send.OkAsync(Map.FromEntity(ticket), ct);
    }
}

public sealed record CancelTicketRequest(string CustomerPhone);

/// <summary>Cancels a waiting or called ticket. The customer must provide their phone number to confirm ownership.</summary>
public sealed class CancelTicketEndpoint(
    IDocumentSession session,
    IHubContext<QueueHub> hub) : Endpoint<CancelTicketRequest>
{
    public override void Configure()
    {
        Patch("/tickets/{id:guid}/cancel");
        Version(1);
        AllowAnonymous();
        Description(x => x.WithTags("Tickets"));
        Options(x => x.WithVersionSet("TeleQ").MapToApiVersion(1.0));
    }

    public override async Task HandleAsync(CancelTicketRequest req, CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var ticket = await session.Events.AggregateStreamAsync<Ticket>(id, token: ct);

        if (ticket is null) { await Send.NotFoundAsync(ct); return; }

        if (ticket.CustomerPhone != req.CustomerPhone)
        {
            await Send.ForbiddenAsync(ct);
            return;
        }

        if (ticket.Status is TicketStatus.Served or TicketStatus.NoShow or TicketStatus.Cancelled)
        {
            AddError($"Cannot cancel a ticket with status '{ticket.Status}'.");
            await Send.ErrorsAsync(409, ct);
            return;
        }

        var evt = new TicketCancelled(
            TicketId: id,
            BranchId: ticket.BranchId,
            ServiceId: ticket.ServiceId,
            CancelledBy: "customer",
            CancelledAt: DateTimeOffset.UtcNow);

        session.Events.Append(id, evt);
        await session.SaveChangesAsync(ct);

        await hub.Clients.Group(QueueHub.GroupName(ticket.BranchId, ticket.ServiceId))
            .SendAsync("TicketCancelled", new { TicketId = id, TicketNumber = ticket.TicketNumber }, ct);

        await Send.NoContentAsync(ct);
    }
}

public sealed record RescheduleTicketRequest(string CustomerPhone, Guid NewTimeSlotId);

/// <summary>Reschedules an appointment ticket to a new time slot. The customer must provide their phone number to confirm ownership.</summary>
public sealed class RescheduleTicketEndpoint(
    AppDbContext db,
    IDocumentSession session) : Endpoint<RescheduleTicketRequest>
{
    public override void Configure()
    {
        Patch("/tickets/{id:guid}/reschedule");
        Version(1);
        AllowAnonymous();
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

        await Send.NoContentAsync(ct);
    }
}
