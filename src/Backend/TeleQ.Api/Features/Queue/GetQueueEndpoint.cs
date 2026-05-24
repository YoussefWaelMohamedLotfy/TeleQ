using FastEndpoints;
using FastEndpoints.AspVersioning;
using Marten;
using Microsoft.EntityFrameworkCore;
using TeleQ.Api.Common.Projections;
using TeleQ.Api.Data;

namespace TeleQ.Api.Features.Queue;

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

        var service = await EntityFrameworkQueryableExtensions
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
