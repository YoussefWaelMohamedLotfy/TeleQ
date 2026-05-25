using FastEndpoints;
using FastEndpoints.AspVersioning;
using Marten;
using Mediator;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Hybrid;
using TeleQ.Api.Common;
using TeleQ.Api.Common.Aggregates;
using TeleQ.Api.Common.DomainEvents;
using TeleQ.Api.Features.Notifications;

namespace TeleQ.Api.Features.Queue;

/// <summary>Marks a called ticket as served, completing the service transaction. Restricted to Clerk and Admin users.</summary>
public sealed class ServeTicketEndpoint(
    IDocumentSession session,
    IHubContext<QueueHub> hub,
    HybridCache cache,
    IMediator mediator) : EndpointWithoutRequest
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
        await cache.RemoveByTagAsync([$"ticket:{id}", $"queue:{ticket.BranchId}:{ticket.ServiceId}"], ct);

        await hub.Clients.Group(QueueHub.GroupName(ticket.BranchId, ticket.ServiceId))
            .SendAsync("TicketServed", new { TicketId = id, TicketNumber = ticket.TicketNumber }, ct);

        // Publish notification — handler runs in its own DI scope, independent of this request.
        await mediator.Publish(new TicketServedNotification(ticket.CustomerPhone, ticket.TicketNumber), ct);

        await Send.NoContentAsync(ct);
    }
}

