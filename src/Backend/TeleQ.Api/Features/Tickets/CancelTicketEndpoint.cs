using FastEndpoints;
using FastEndpoints.AspVersioning;
using Marten;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Hybrid;
using TeleQ.Api.Common;
using TeleQ.Messaging.Shared.Aggregates;
using TeleQ.Messaging.Shared.DomainEvents;
using TeleQ.Api.Features.Notifications;

namespace TeleQ.Api.Features.Tickets;

/// <summary>Cancels a waiting or called ticket. The customer must provide their phone number to confirm ownership.</summary>
public sealed class CancelTicketEndpoint(
    IDocumentSession session,
    IHubContext<QueueHub> hub,
    HybridCache cache) : Endpoint<CancelTicketRequest>
{
    public override void Configure()
    {
        Patch("/tickets/{id:guid}/cancel");
        Version(1);
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
        await cache.RemoveByTagAsync([$"ticket:{id}", $"queue:{ticket.BranchId}:{ticket.ServiceId}"], ct);

        await hub.Clients.Group(QueueHub.GroupName(ticket.BranchId, ticket.ServiceId))
            .SendAsync("TicketCancelled", new { TicketId = id, TicketNumber = ticket.TicketNumber }, ct);

        await Send.NoContentAsync(ct);
    }
}
