using FastEndpoints;
using TeleQ.Api.Data.Entities;

namespace TeleQ.Api.Features.TimeSlots;

/// <summary>Maps between <see cref="CreateTimeSlotRequest"/> and <see cref="TimeSlot"/> entities, and from <see cref="TimeSlot"/> to <see cref="TimeSlotResponse"/>.</summary>
public sealed class TimeSlotMapper : Mapper<CreateTimeSlotRequest, TimeSlotResponse, TimeSlot>
{
    public override TimeSlot ToEntity(CreateTimeSlotRequest req) => new()
    {
        Id = Guid.CreateVersion7(),
        BranchId = req.BranchId,
        StartTime = req.StartTime,
        EndTime = req.EndTime,
        Capacity = req.Capacity,
        IsRecurring = req.IsRecurring,
        DayOfWeek = req.DayOfWeek,
        Date = req.Date,
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow
        // ServiceId is set from the route parameter in the endpoint
    };

    public override TimeSlotResponse FromEntity(TimeSlot ts) =>
        new(ts.Id, ts.ServiceId, ts.BranchId, ts.StartTime, ts.EndTime,
            ts.Capacity, ts.BookedCount, ts.Capacity - ts.BookedCount,
            ts.IsRecurring, ts.DayOfWeek, ts.Date, ts.IsActive);
}
