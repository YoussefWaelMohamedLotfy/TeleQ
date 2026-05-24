using FastEndpoints;
using FastEndpoints.AspVersioning;
using Marten;
using Microsoft.Extensions.Caching.Hybrid;
using TeleQ.Api.Common;
using TeleQ.Api.Common.Projections;

namespace TeleQ.Api.Features.Reports;

/// <summary>Returns aggregated queue statistics for a specific branch, service, and date. Restricted to Admin users.</summary>
public sealed class GetDailyStatsEndpoint(IDocumentSession session, HybridCache cache)
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

        var result = await cache.GetOrCreateAsync(
            CacheKeys.DailyStats(date, branchId, serviceId),
            async ct =>
            {
                return await session.LoadAsync<DailyQueueStats>(statsId, ct)
                    ?? new DailyQueueStats { Id = statsId, Date = date, BranchId = branchId, ServiceId = serviceId };
            },
            CacheOptions.Stats,
            CacheKeys.StatsTags(branchId, serviceId),
            ct);

        await Send.OkAsync(result, ct);
    }
}
