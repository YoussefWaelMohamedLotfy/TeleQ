using FastEndpoints;
using FastEndpoints.AspVersioning;
using Marten;
using Microsoft.Extensions.Caching.Hybrid;
using TeleQ.Api.Common;
using TeleQ.Api.Common.Projections;

namespace TeleQ.Api.Features.Reports;

/// <summary>Returns daily queue statistics across a date range for a branch and service. Restricted to Admin users.</summary>
public sealed class GetDailyStatsRangeEndpoint(IDocumentSession session, HybridCache cache)
    : Endpoint<DailyStatsRangeRequest, List<DailyQueueStats>>
{
    public override void Configure()
    {
        Get("/reports/daily-stats/range");
        Version(1);
        Policies("AdminOnly");
        Description(x => x.WithTags("Reports"));
        Options(x => x.WithVersionSet("TeleQ").MapToApiVersion(1.0));
    }

    public override async Task HandleAsync(DailyStatsRangeRequest req, CancellationToken ct)
    {
        var from = req.From ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-6).Date);
        var to = req.To ?? DateOnly.FromDateTime(DateTime.UtcNow.Date);

        var result = await cache.GetOrCreateAsync(
            CacheKeys.DailyStatsRange(from, to, req.BranchId, req.ServiceId),
            async ct =>
            {
                var results = new List<DailyQueueStats>();

                for (var day = from; day <= to; day = day.AddDays(1))
                {
                    var statsId = $"{day:yyyyMMdd}:{req.BranchId}:{req.ServiceId}";
                    var stats = await session.LoadAsync<DailyQueueStats>(statsId, ct);
                    results.Add(stats ?? new DailyQueueStats
                    {
                        Id = statsId,
                        Date = day,
                        BranchId = req.BranchId,
                        ServiceId = req.ServiceId
                    });
                }

                return results;
            },
            CacheOptions.Stats,
            CacheKeys.StatsTags(req.BranchId, req.ServiceId),
            ct);

        await Send.OkAsync(result, ct);
    }
}
