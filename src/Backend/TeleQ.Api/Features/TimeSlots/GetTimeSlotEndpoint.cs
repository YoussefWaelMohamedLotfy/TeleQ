using FastEndpoints;
using FastEndpoints.AspVersioning;
using Microsoft.Extensions.Caching.Hybrid;
using TeleQ.Api.Common;
using TeleQ.Api.Data;

namespace TeleQ.Api.Features.TimeSlots;

/// <summary>Returns a single time slot by its identifier.</summary>
public sealed class GetTimeSlotEndpoint(AppDbContext db, HybridCache cache)
    : EndpointWithoutRequest<TimeSlotResponse, TimeSlotMapper>
{
    public override void Configure()
    {
        Get("/timeslots/{id:guid}");
        Version(1);
        Description(x => x.WithTags("Time Slots"));
        Options(x => x.WithVersionSet("TeleQ").MapToApiVersion(1.0));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");

        var result = await cache.GetOrCreateAsync<TimeSlotResponse?>(
            CacheKeys.TimeSlot(id),
            async ct =>
            {
                var slot = await db.TimeSlots.FindAsync([id], ct);
                return slot is null ? null : Map.FromEntity(slot);
            },
            CacheOptions.Static,
            tags: ["timeslots", $"timeslot:{id}"],
            cancellationToken: ct);

        if (result is null) { await Send.NotFoundAsync(ct); return; }
        await Send.OkAsync(result, ct);
    }
}
