using FastEndpoints;
using FastEndpoints.AspVersioning;
using Microsoft.EntityFrameworkCore;
using TeleQ.Api.Data;

namespace TeleQ.Api.Features.TimeSlots;

public sealed record TimeSlotResponse(
    Guid Id,
    Guid ServiceId,
    Guid BranchId,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int Capacity,
    int BookedCount,
    int AvailableCount,
    bool IsRecurring,
    DayOfWeek? DayOfWeek,
    DateOnly? Date,
    bool IsActive);

public sealed record CreateTimeSlotRequest(
    Guid BranchId,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int Capacity,
    bool IsRecurring,
    DayOfWeek? DayOfWeek,
    DateOnly? Date);

/// <summary>Returns all active time slots for a service, ordered by day and start time.</summary>
public sealed class GetTimeSlotsEndpoint(AppDbContext db, TimeSlotMapper mapper)
    : EndpointWithoutRequest<List<TimeSlotResponse>>
{
    public override void Configure()
    {
        Get("/services/{serviceId:guid}/timeslots");
        Version(1);
        AllowAnonymous();
        Description(x => x.WithTags("Time Slots"));
        Options(x => x.WithVersionSet("TeleQ").MapToApiVersion(1.0));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var serviceId = Route<Guid>("serviceId");

        var slots = await db.TimeSlots
            .Where(ts => ts.ServiceId == serviceId && ts.IsActive)
            .OrderBy(ts => ts.DayOfWeek)
            .ThenBy(ts => ts.StartTime)
            .ToListAsync(ct);

        await Send.OkAsync(slots.Select(mapper.FromEntity).ToList(), ct);
    }
}

/// <summary>Returns a single time slot by its identifier.</summary>
public sealed class GetTimeSlotEndpoint(AppDbContext db)
    : EndpointWithoutRequest<TimeSlotResponse, TimeSlotMapper>
{
    public override void Configure()
    {
        Get("/timeslots/{id:guid}");
        Version(1);
        AllowAnonymous();
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

        if (!await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
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

/// <summary>Soft-deletes (deactivates) a time slot. Restricted to Admin users.</summary>
public sealed class DeleteTimeSlotEndpoint(AppDbContext db) : EndpointWithoutRequest
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

        slot.IsActive = false;
        await db.SaveChangesAsync(ct);
        await Send.NoContentAsync(ct);
    }
}
