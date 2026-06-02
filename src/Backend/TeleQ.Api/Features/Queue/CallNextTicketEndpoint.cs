using FastEndpoints;
using FastEndpoints.AspVersioning;
using Marten;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Hybrid;
using TeleQ.Api.Common;
using TeleQ.Messaging.Shared.DomainEvents;
using TeleQ.Api.Common.Projections;
using TeleQ.Api.Features.Notifications;

namespace TeleQ.Api.Features.Queue;

/// <summary>Calls the next waiting ticket in the queue for the specified service. Restricted to Clerk and Admin users.</summary>
public sealed class CallNextTicketEndpoint(
    IDocumentSession session,
    IHubContext<QueueHub> hub,
    HybridCache cache) : Endpoint<CallNextRequest, CallNextResponse>
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
                      ?? Guid.CreateVersion7().ToString();

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
        await cache.RemoveByTagAsync([$"queue:{req.BranchId}:{req.ServiceId}", $"ticket:{next.TicketId}"], ct);

        var response = new CallNextResponse(
            next.TicketId, next.TicketNumber, next.CustomerPhone,
            counterLabel, DateTimeOffset.UtcNow);

        await hub.Clients.Group(QueueHub.GroupName(req.BranchId, req.ServiceId))
            .SendAsync("TicketCalled", response, ct);

        await Send.OkAsync(response, ct);
    }
}
