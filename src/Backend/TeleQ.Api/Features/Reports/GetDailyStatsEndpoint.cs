using FastEndpoints;
using FastEndpoints.AspVersioning;
using Marten;
using Microsoft.Extensions.Caching.Hybrid;
using TeleQ.Api.Common;
using TeleQ.Api.Common.Projections;

namespace TeleQ.Api.Features.Reports;

/// <summary>Returns aggregated queue statistics for a specific branch, service, and date. Accessible by clerks and admins.</summary>
public sealed class GetDailyStatsEndpoint(IDocumentSession session, HybridCache cache)
    : Endpoint<DailyStatsRequest, DailyQueueStats>
{
    public override void Configure()
    {
        Get("/reports/daily-stats");
        Version(1);
        Policies("ClerkOrAdmin");
        Description(x => x.WithTags("Reports"));
        Options(x => x.WithVersionSet("TeleQ").MapToApiVersion(1.0));
    }

    public override async Task HandleAsync(DailyStatsRequest req, CancellationToken ct)
    {
        var date = req.Date ?? DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var statsId = $"{date:yyyyMMdd}:{req.BranchId}:{req.ServiceId}";

        var result = await cache.GetOrCreateAsync(
            CacheKeys.DailyStats(date, req.BranchId, req.ServiceId),
            async ct =>
            {
                return await session.LoadAsync<DailyQueueStats>(statsId, ct)
                    ?? new DailyQueueStats { Id = statsId, Date = date, BranchId = req.BranchId, ServiceId = req.ServiceId };
            },
            CacheOptions.Stats,
            CacheKeys.StatsTags(req.BranchId, req.ServiceId),
            ct);

        await Send.OkAsync(result, ct);
    }
}
