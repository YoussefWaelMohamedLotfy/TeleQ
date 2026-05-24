using FastEndpoints;
using FastEndpoints.AspVersioning;
using Microsoft.EntityFrameworkCore;
using TeleQ.Api.Data;
using TeleQ.Api.Data.Entities;

namespace TeleQ.Api.Features.TimeSlots;

// ── Shared DTOs ───────────────────────────────────────────────────────────

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

public static class TimeSlotMapper
{
    public static TimeSlotResponse ToResponse(this TimeSlot ts) =>
        new(ts.Id, ts.ServiceId, ts.BranchId, ts.StartTime, ts.EndTime,
            ts.Capacity, ts.BookedCount, ts.Capacity - ts.BookedCount,
            ts.IsRecurring, ts.DayOfWeek, ts.Date, ts.IsActive);
}

// ── GET /services/{serviceId}/timeslots ───────────────────────────────────

public sealed class GetTimeSlotsEndpoint(AppDbContext db) : EndpointWithoutRequest<List<TimeSlotResponse>>
{
    public override void Configure()
    {
        Get("/services/{serviceId:guid}/timeslots");
        Version(1);
        AllowAnonymous();
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

        await Send.OkAsync(slots.Select(ts => ts.ToResponse()).ToList(), ct);
    }
}

// ── GET /timeslots/{id} ───────────────────────────────────────────────────

public sealed class GetTimeSlotEndpoint(AppDbContext db) : EndpointWithoutRequest<TimeSlotResponse>
{
    public override void Configure()
    {
        Get("/timeslots/{id:guid}");
        Version(1);
        AllowAnonymous();
        Options(x => x.WithVersionSet("TeleQ").MapToApiVersion(1.0));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var slot = await db.TimeSlots.FindAsync([id], ct);

        if (slot is null) { await Send.NotFoundAsync(ct); return; }

        await Send.OkAsync(slot.ToResponse(), ct);
    }
}

// ── POST /services/{serviceId}/timeslots (Admin only) ─────────────────────

public sealed record CreateTimeSlotRequest(
    Guid BranchId,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int Capacity,
    bool IsRecurring,
    DayOfWeek? DayOfWeek,
    DateOnly? Date);

public sealed class CreateTimeSlotEndpoint(AppDbContext db) : Endpoint<CreateTimeSlotRequest, TimeSlotResponse>
{
    public override void Configure()
    {
        Post("/services/{serviceId:guid}/timeslots");
        Version(1);
        Policies("AdminOnly");
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

        var slot = new TimeSlot
        {
            Id = Guid.NewGuid(),
            ServiceId = serviceId,
            BranchId = req.BranchId,
            StartTime = req.StartTime,
            EndTime = req.EndTime,
            Capacity = req.Capacity,
            IsRecurring = req.IsRecurring,
            DayOfWeek = req.DayOfWeek,
            Date = req.Date,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.TimeSlots.Add(slot);
        await db.SaveChangesAsync(ct);

        await Send.CreatedAtAsync<GetTimeSlotEndpoint>(
            new { id = slot.Id },
            slot.ToResponse(),
            cancellation: ct);
    }
}

// ── DELETE /timeslots/{id} (Admin only) ───────────────────────────────────

public sealed class DeleteTimeSlotEndpoint(AppDbContext db) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/timeslots/{id:guid}");
        Version(1);
        Policies("AdminOnly");
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
