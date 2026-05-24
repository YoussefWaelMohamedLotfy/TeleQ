using FastEndpoints;
using FastEndpoints.AspVersioning;
using Microsoft.EntityFrameworkCore;
using TeleQ.Api.Data;

namespace TeleQ.Api.Features.TimeSlots;

/// <summary>Creates a new time slot for a service. Restricted to Admin users.</summary>
public sealed class CreateTimeSlotEndpoint(AppDbContext db)
    : Endpoint<CreateTimeSlotRequest, TimeSlotResponse, TimeSlotMapper>
{
    public override void Configure()
    {
        Post("/services/{serviceId:guid}/timeslots");
        Version(1);
        Policies("AdminOnly");
        Description(x => x.WithTags("Time Slots"));
        Options(x => x.WithVersionSet("TeleQ").MapToApiVersion(1.0));
    }

    public override async Task HandleAsync(CreateTimeSlotRequest req, CancellationToken ct)
    {
        var serviceId = Route<Guid>("serviceId");

        if (!await EntityFrameworkQueryableExtensions
            .AnyAsync(db.Services, s => s.Id == serviceId && s.IsActive, ct))
        {
            AddError("Service not found or inactive.");
            await Send.ErrorsAsync(404, ct);
            return;
        }

        if (!req.IsRecurring && req.Date is null)
        {
            AddError("A one-off slot must have a Date.");
            await Send.ErrorsAsync(400, ct);
            return;
        }

        if (req.IsRecurring && req.DayOfWeek is null)
        {
            AddError("A recurring slot must have a DayOfWeek.");
            await Send.ErrorsAsync(400, ct);
            return;
        }

        var slot = Map.ToEntity(req);
        slot.ServiceId = serviceId;

        db.TimeSlots.Add(slot);
        await db.SaveChangesAsync(ct);

        await Send.CreatedAtAsync<GetTimeSlotEndpoint>(
            new { id = slot.Id },
            Map.FromEntity(slot),
            cancellation: ct);
    }
}
