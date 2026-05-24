using FastEndpoints;
using FastEndpoints.AspVersioning;
using TeleQ.Api.Data;

namespace TeleQ.Api.Features.TimeSlots;

/// <summary>Returns a single time slot by its identifier.</summary>
public sealed class GetTimeSlotEndpoint(AppDbContext db)
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
        var slot = await db.TimeSlots.FindAsync([id], ct);

        if (slot is null) { await Send.NotFoundAsync(ct); return; }

        await Send.OkAsync(Map.FromEntity(slot), ct);
    }
}
