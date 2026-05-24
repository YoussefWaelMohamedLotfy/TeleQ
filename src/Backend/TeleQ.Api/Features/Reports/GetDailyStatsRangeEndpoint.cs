using FastEndpoints;
using FastEndpoints.AspVersioning;
using Marten;
using Microsoft.Extensions.Caching.Hybrid;
using TeleQ.Api.Common;
using TeleQ.Api.Common.Projections;

namespace TeleQ.Api.Features.Reports;

/// <summary>Returns daily queue statistics across a date range for a branch and service. Restricted to Admin users.</summary>
public sealed class GetDailyStatsRangeEndpoint(IDocumentSession session, HybridCache cache)
    : EndpointWithoutRequest<List<DailyQueueStats>>
{
    public override void Configure()
    {
        Get("/reports/daily-stats/range");
        Version(1);
        Policies("AdminOnly");
        Description(x => x.WithTags("Reports"));
        Options(x => x.WithVersionSet("TeleQ").MapToApiVersion(1.0));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var branchId = Query<Guid>("branchId");
        var serviceId = Query<Guid>("serviceId");
        var from = Query<DateOnly?>("from") ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-6).Date);
        var to = Query<DateOnly?>("to") ?? DateOnly.FromDateTime(DateTime.UtcNow.Date);

        var result = await cache.GetOrCreateAsync(
            CacheKeys.DailyStatsRange(from, to, branchId, serviceId),
            async ct =>
            {
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

                return results;
            },
            CacheOptions.Stats,
            CacheKeys.StatsTags(branchId, serviceId),
            ct);

        await Send.OkAsync(result, ct);
    }
}
