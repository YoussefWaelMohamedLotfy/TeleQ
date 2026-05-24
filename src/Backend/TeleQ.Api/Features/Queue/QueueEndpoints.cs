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

/// <summary>Returns the current live queue state for a specific branch and service combination.</summary>
public sealed class GetQueueEndpoint(IDocumentSession session, AppDbContext db)
    : EndpointWithoutRequest<QueueResponse>
{
    public override void Configure()
    {
        Get("/queue/{branchId:guid}/{serviceId:guid}");
        Version(1);
        Policies("AnyStaff");
        Description(x => x.WithTags("Queue"));
        Options(x => x.WithVersionSet("TeleQ").MapToApiVersion(1.0));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var branchId = Route<Guid>("branchId");
        var serviceId = Route<Guid>("serviceId");

        var queueId = $"{branchId}:{serviceId}";
        var snapshot = await session.LoadAsync<BranchQueueSnapshot>(queueId, ct);

        var service = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .FirstOrDefaultAsync(db.Services, s => s.Id == serviceId && s.BranchId == branchId, ct);

        var durationPerTicket = service?.EstimatedDurationMinutes ?? 10;

        if (snapshot is null)
        {
            await Send.OkAsync(new QueueResponse(branchId, serviceId, [], [], 0, 0, 0, 0), ct);
            return;
        }

        var waiting = snapshot.WaitingTickets
            .OrderBy(t => t.QueuePosition)
            .Select((t, i) => new QueueEntryResponse(
                t.TicketId, t.TicketNumber, t.CustomerPhone,
                t.QueuePosition, t.IssuedAt, t.ScheduledAt,
                t.Type.ToString(),
                EstimatedWaitMinutes: (i + 1) * durationPerTicket))
            .ToList();

        var called = snapshot.CalledTickets
            .Select(t => new QueueEntryResponse(
                t.TicketId, t.TicketNumber, t.CustomerPhone,
                t.QueuePosition, t.IssuedAt, t.ScheduledAt,
                t.Type.ToString(),
                EstimatedWaitMinutes: 0))
            .ToList();

        var estimatedWait = waiting.Count * durationPerTicket;

        await Send.OkAsync(new QueueResponse(
            branchId, serviceId, waiting, called,
            snapshot.TotalServedToday, snapshot.TotalNoShowToday,
            snapshot.TotalCancelledToday, estimatedWait), ct);
    }
}

public sealed record MyPositionResponse(
    Guid TicketId,
    string TicketNumber,
    string Status,
    int QueuePosition,
    int AheadCount,
    int EstimatedWaitMinutes);

/// <summary>Returns the queue position and estimated wait time for a specific ticket.</summary>
public sealed class GetMyQueuePositionEndpoint(IDocumentSession session, AppDbContext db)
    : EndpointWithoutRequest<MyPositionResponse>
{
    public override void Configure()
    {
        Get("/queue/my-position");
        Version(1);
        AllowAnonymous();
        Description(d => d.WithTags("Queue").WithSummary("Get position and estimated wait for a specific ticket"));
        Options(x => x.WithVersionSet("TeleQ").MapToApiVersion(1.0));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var ticketId = Query<Guid>("ticketId");
        var ticket = await session.Events.AggregateStreamAsync<Ticket>(ticketId, token: ct);

        if (ticket is null) { await Send.NotFoundAsync(ct); return; }

        var queueId = $"{ticket.BranchId}:{ticket.ServiceId}";
        var snapshot = await session.LoadAsync<BranchQueueSnapshot>(queueId, ct);

        var service = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .FirstOrDefaultAsync(db.Services, s => s.Id == ticket.ServiceId, ct);

        var durationPerTicket = service?.EstimatedDurationMinutes ?? 10;

        var aheadCount = snapshot?.WaitingTickets
            .Count(t => t.QueuePosition < ticket.QueuePosition) ?? 0;

        await Send.OkAsync(new MyPositionResponse(
            ticket.Id, ticket.TicketNumber, ticket.Status.ToString(),
            ticket.QueuePosition, aheadCount, aheadCount * durationPerTicket), ct);
    }
}

public sealed record CallNextRequest(Guid BranchId, Guid ServiceId);

public sealed record CallNextResponse(
    Guid TicketId,
    string TicketNumber,
    string CustomerPhone,
    string CounterLabel,
    DateTimeOffset CalledAt);

/// <summary>Calls the next waiting ticket in the queue for the specified service. Restricted to Clerk and Admin users.</summary>
public sealed class CallNextTicketEndpoint(
    IDocumentSession session,
    IHubContext<QueueHub> hub) : Endpoint<CallNextRequest, CallNextResponse>
{
    public override void Configure()
    {
        Post("/queue/call-next");
        Version(1);
        Policies("ClerkOrAdmin");
        Description(x => x.WithTags("Queue"));
        Options(x => x.WithVersionSet("TeleQ").MapToApiVersion(1.0));
    }

    public override async Task HandleAsync(CallNextRequest req, CancellationToken ct)
    {
        var clerkId = User.FindFirst("sub")?.Value
                      ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                      ?? Guid.NewGuid().ToString();

        var counterLabel = User.FindFirst("counter_label")?.Value ?? "Counter";

        var queueId = $"{req.BranchId}:{req.ServiceId}";
        var snapshot = await session.LoadAsync<BranchQueueSnapshot>(queueId, ct);

        var next = snapshot?.WaitingTickets.MinBy(t => t.QueuePosition);

        if (next is null)
        {
            AddError("No waiting tickets in this queue.");
            await Send.ErrorsAsync(404, ct);
            return;
        }

        var evt = new TicketCalled(
            TicketId: next.TicketId,
            BranchId: req.BranchId,
            ServiceId: req.ServiceId,
            ClerkId: Guid.Parse(clerkId),
            CounterLabel: counterLabel,
            CalledAt: DateTimeOffset.UtcNow);

        session.Events.Append(next.TicketId, evt);
        await session.SaveChangesAsync(ct);

        var response = new CallNextResponse(
            next.TicketId, next.TicketNumber, next.CustomerPhone,
            counterLabel, DateTimeOffset.UtcNow);

        await hub.Clients.Group(QueueHub.GroupName(req.BranchId, req.ServiceId))
            .SendAsync("TicketCalled", response, ct);

        await Send.OkAsync(response, ct);
    }
}

/// <summary>Marks a called ticket as served, completing the service transaction. Restricted to Clerk and Admin users.</summary>
public sealed class ServeTicketEndpoint(
    IDocumentSession session,
    IHubContext<QueueHub> hub) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/queue/tickets/{id:guid}/serve");
        Version(1);
        Policies("ClerkOrAdmin");
        Description(x => x.WithTags("Queue"));
        Options(x => x.WithVersionSet("TeleQ").MapToApiVersion(1.0));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var clerkId = User.FindFirst("sub")?.Value
                      ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                      ?? Guid.NewGuid().ToString();

        var ticket = await session.Events.AggregateStreamAsync<Ticket>(id, token: ct);

        if (ticket is null) { await Send.NotFoundAsync(ct); return; }

        if (ticket.Status != TicketStatus.Called)
        {
            AddError("Only a Called ticket can be marked as Served.");
            await Send.ErrorsAsync(409, ct);
            return;
        }

        var evt = new TicketServed(
            TicketId: id,
            BranchId: ticket.BranchId,
            ServiceId: ticket.ServiceId,
            ClerkId: Guid.Parse(clerkId),
            ServedAt: DateTimeOffset.UtcNow);

        session.Events.Append(id, evt);
        await session.SaveChangesAsync(ct);

        await hub.Clients.Group(QueueHub.GroupName(ticket.BranchId, ticket.ServiceId))
            .SendAsync("TicketServed", new { TicketId = id, TicketNumber = ticket.TicketNumber }, ct);

        await Send.NoContentAsync(ct);
    }
}

/// <summary>Marks a waiting or called ticket as no-show. Restricted to Clerk and Admin users.</summary>
public sealed class NoShowTicketEndpoint(
    IDocumentSession session,
    IHubContext<QueueHub> hub) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/queue/tickets/{id:guid}/no-show");
        Version(1);
        Policies("ClerkOrAdmin");
        Description(x => x.WithTags("Queue"));
        Options(x => x.WithVersionSet("TeleQ").MapToApiVersion(1.0));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var clerkId = User.FindFirst("sub")?.Value
                      ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        var ticket = await session.Events.AggregateStreamAsync<Ticket>(id, token: ct);

        if (ticket is null) { await Send.NotFoundAsync(ct); return; }

        if (ticket.Status is not (TicketStatus.Called or TicketStatus.Waiting))
        {
            AddError($"Cannot mark a '{ticket.Status}' ticket as No-Show.");
            await Send.ErrorsAsync(409, ct);
            return;
        }

        var evt = new TicketNoShow(
            TicketId: id,
            BranchId: ticket.BranchId,
            ServiceId: ticket.ServiceId,
            ClerkId: clerkId is null ? null : Guid.Parse(clerkId),
            MarkedAt: DateTimeOffset.UtcNow);

        session.Events.Append(id, evt);
        await session.SaveChangesAsync(ct);

        await hub.Clients.Group(QueueHub.GroupName(ticket.BranchId, ticket.ServiceId))
            .SendAsync("TicketNoShow", new { TicketId = id, TicketNumber = ticket.TicketNumber }, ct);

        await Send.NoContentAsync(ct);
    }
}
