using FastEndpoints;
using FastEndpoints.AspVersioning;
using Microsoft.Extensions.Caching.Hybrid;
using TeleQ.Api.Common;
using TeleQ.Api.Data;

namespace TeleQ.Api.Features.TimeSlots;

/// <summary>Soft-deletes (deactivates) a time slot. Restricted to Admin users.</summary>
public sealed class DeleteTimeSlotEndpoint(AppDbContext db, HybridCache cache) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/timeslots/{id:guid}");
        Version(1);
        Policies("AdminOnly");
        Description(x => x.WithTags("Time Slots"));
        Options(x => x.WithVersionSet("TeleQ").MapToApiVersion(1.0));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var slot = await db.TimeSlots.FindAsync([id], ct);

        if (slot is null) { await Send.NotFoundAsync(ct); return; }

        var serviceId = slot.ServiceId;
        slot.IsActive = false;
        await db.SaveChangesAsync(ct);
        await cache.RemoveByTagAsync(["timeslots", $"timeslots:service:{serviceId}", $"timeslot:{id}"], ct);
        await Send.NoContentAsync(ct);
    }
}
