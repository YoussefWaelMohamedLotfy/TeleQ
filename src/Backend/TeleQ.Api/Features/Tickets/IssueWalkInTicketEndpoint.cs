using FastEndpoints;
using FastEndpoints.AspVersioning;
using Marten;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using TeleQ.Api.Common;
using TeleQ.Api.Common.Aggregates;
using TeleQ.Api.Common.DomainEvents;
using TeleQ.Api.Common.Projections;
using TeleQ.Api.Data;
using TeleQ.Api.Features.Notifications;

namespace TeleQ.Api.Features.Tickets;

/// <summary>Issues a walk-in ticket, placing the customer in the queue immediately.</summary>
public sealed class IssueWalkInTicketEndpoint(
    AppDbContext db,
    IDocumentSession session,
    IHubContext<QueueHub> hub,
    TicketMapper mapper,
    HybridCache cache) : Endpoint<IssueWalkInTicketRequest, TicketResponse>
{
    public override void Configure()
    {
        Post("/tickets/walkin");
        Version(1);
        Description(x => x.WithTags("Tickets"));
        Options(x => x.WithVersionSet("TeleQ").MapToApiVersion(1.0));
    }

    public override async Task HandleAsync(IssueWalkInTicketRequest req, CancellationToken ct)
    {
        if (!await EntityFrameworkQueryableExtensions
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
        await cache.RemoveByTagAsync($"queue:{req.BranchId}:{req.ServiceId}", ct);

        var ticket = await session.Events.AggregateStreamAsync<Ticket>(ticketId, token: ct);

        await hub.Clients.Group(QueueHub.GroupName(req.BranchId, req.ServiceId))
            .SendAsync("QueueUpdated", new { TicketNumber = ticketNumber, QueuePosition = queuePosition }, ct);

        await Send.CreatedAtAsync<GetTicketEndpoint>(
            new { id = ticketId },
            mapper.FromEntity(ticket!),
            cancellation: ct);
    }
}
