using FastEndpoints;
using FastEndpoints.AspVersioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using TeleQ.Api.Common;
using TeleQ.Api.Data;

namespace TeleQ.Api.Features.TimeSlots;

/// <summary>Returns all active time slots for a service, ordered by day and start time.</summary>
public sealed class GetTimeSlotsEndpoint(AppDbContext db, TimeSlotMapper mapper, HybridCache cache)
    : EndpointWithoutRequest<List<TimeSlotResponse>>
{
    public override void Configure()
    {
        Get("/services/{serviceId:guid}/timeslots");
        Version(1);
        Description(x => x.WithTags("Time Slots"));
        Options(x => x.WithVersionSet("TeleQ").MapToApiVersion(1.0));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var serviceId = Route<Guid>("serviceId");

        var result = await cache.GetOrCreateAsync(
            CacheKeys.TimeSlotList(serviceId),
            async ct =>
            {
                var slots = await db.TimeSlots
                    .Where(ts => ts.ServiceId == serviceId && ts.IsActive)
                    .OrderBy(ts => ts.DayOfWeek)
                    .ThenBy(ts => ts.StartTime)
                    .ToListAsync(ct);

                return slots.Select(mapper.FromEntity).ToList();
            },
            CacheOptions.Static,
            CacheKeys.TimeSlotListTags(serviceId),
            ct);

        await Send.OkAsync(result, ct);
    }
}
