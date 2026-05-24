using FastEndpoints;
using FastEndpoints.AspVersioning;
using Marten;
using TeleQ.Api.Common.Projections;

namespace TeleQ.Api.Features.Reports;

/// <summary>Returns aggregated queue statistics for a specific branch, service, and date. Restricted to Admin users.</summary>
public sealed class GetDailyStatsEndpoint(IDocumentSession session)
    : EndpointWithoutRequest<DailyQueueStats>
{
    public override void Configure()
    {
        Get("/reports/daily-stats");
        Version(1);
        Policies("AdminOnly");
        Description(x => x.WithTags("Reports"));
        Options(x => x.WithVersionSet("TeleQ").MapToApiVersion(1.0));
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
