using FastEndpoints;
using FastEndpoints.AspVersioning;
using Marten;
using Microsoft.AspNetCore.SignalR;
using TeleQ.Api.Common.Aggregates;
using TeleQ.Api.Common.DomainEvents;
using TeleQ.Api.Features.Notifications;

namespace TeleQ.Api.Features.Queue;

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
