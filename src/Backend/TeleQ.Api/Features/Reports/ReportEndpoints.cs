using FastEndpoints;
using Marten;
using TeleQ.Api.Common.Projections;

namespace TeleQ.Api.Features.Reports;

// ── GET /reports/tickets/{id}/events (Admin only) ─────────────────────────

public sealed record TicketEventEntry(
    string EventType,
    object Data,
    DateTimeOffset Timestamp,
    long Version);

public sealed class GetTicketEventLogEndpoint(IDocumentSession session)
    : EndpointWithoutRequest<List<TicketEventEntry>>
{
    public override void Configure()
    {
        Get("/reports/tickets/{id:guid}/events");
        Version(1);
        Policies("AdminOnly");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var events = await session.Events.FetchStreamAsync(id, token: ct);

        if (!events.Any())
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var result = events
            .Select(e => new TicketEventEntry(
                e.EventTypeName,
                e.Data,
                e.Timestamp,
                e.Version))
            .ToList();

        await Send.OkAsync(result, ct);
    }
}

// ── GET /reports/daily-stats?branchId=...&serviceId=...&date=... (Admin only) ──

public sealed class GetDailyStatsEndpoint(IDocumentSession session)
    : EndpointWithoutRequest<DailyQueueStats>
{
    public override void Configure()
    {
        Get("/reports/daily-stats");
        Version(1);
        Policies("AdminOnly");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var branchId = Query<Guid>("branchId");
        var serviceId = Query<Guid>("serviceId");
        var date = Query<DateOnly?>("date") ?? DateOnly.FromDateTime(DateTime.UtcNow.Date);

        var statsId = $"{date:yyyyMMdd}:{branchId}:{serviceId}";
        var stats = await session.LoadAsync<DailyQueueStats>(statsId, ct);

        if (stats is null)
        {
            // Return empty stats for the requested day
            await Send.OkAsync(new DailyQueueStats
            {
                Id = statsId,
                Date = date,
                BranchId = branchId,
                ServiceId = serviceId
            }, ct);
            return;
        }

        await Send.OkAsync(stats, ct);
    }
}

// ── GET /reports/daily-stats/range (Admin only) ──────────────────────────

public sealed class GetDailyStatsRangeEndpoint(IDocumentSession session)
    : EndpointWithoutRequest<List<DailyQueueStats>>
{
    public override void Configure()
    {
        Get("/reports/daily-stats/range");
        Version(1);
        Policies("AdminOnly");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var branchId = Query<Guid>("branchId");
        var serviceId = Query<Guid>("serviceId");
        var from = Query<DateOnly?>("from") ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-6).Date);
        var to = Query<DateOnly?>("to") ?? DateOnly.FromDateTime(DateTime.UtcNow.Date);

        var results = new List<DailyQueueStats>();

        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var statsId = $"{day:yyyyMMdd}:{branchId}:{serviceId}";
            var stats = await session.LoadAsync<DailyQueueStats>(statsId, ct);
            results.Add(stats ?? new DailyQueueStats
            {
                Id = statsId,
                Date = day,
                BranchId = branchId,
                ServiceId = serviceId
            });
        }

        await Send.OkAsync(results, ct);
    }
}
