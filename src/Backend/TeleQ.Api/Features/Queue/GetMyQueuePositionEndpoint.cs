using FastEndpoints;
using FastEndpoints.AspVersioning;
using Marten;
using Microsoft.EntityFrameworkCore;
using TeleQ.Api.Common.Aggregates;
using TeleQ.Api.Common.Projections;
using TeleQ.Api.Data;

namespace TeleQ.Api.Features.Queue;

/// <summary>Returns the queue position and estimated wait time for a specific ticket.</summary>
public sealed class GetMyQueuePositionEndpoint(IDocumentSession session, AppDbContext db)
    : EndpointWithoutRequest<MyPositionResponse>
{
    public override void Configure()
    {
        Get("/queue/my-position");
        Version(1);
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

        var service = await EntityFrameworkQueryableExtensions
            .FirstOrDefaultAsync(db.Services, s => s.Id == ticket.ServiceId, ct);

        var durationPerTicket = service?.EstimatedDurationMinutes ?? 10;

        var aheadCount = snapshot?.WaitingTickets
            .Count(t => t.QueuePosition < ticket.QueuePosition) ?? 0;

        await Send.OkAsync(new MyPositionResponse(
            ticket.Id, ticket.TicketNumber, ticket.Status.ToString(),
            ticket.QueuePosition, aheadCount, aheadCount * durationPerTicket), ct);
    }
}
