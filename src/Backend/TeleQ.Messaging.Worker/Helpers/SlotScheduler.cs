using TeleQ.Messaging.Worker.Data.Entities;

namespace TeleQ.Messaging.Worker.Helpers;

public static class SlotScheduler
{
    /// <summary>
    /// Calculates the next scheduled <see cref="DateTimeOffset"/> for the given time slot.
    /// One-off slots use their fixed Date; recurring slots find the next occurrence.
    /// </summary>
    public static DateTimeOffset NextOccurrence(TimeSlot slot)
    {
        if (!slot.IsRecurring && slot.Date.HasValue)
        {
            return new DateTimeOffset(
                slot.Date.Value.Year, slot.Date.Value.Month, slot.Date.Value.Day,
                slot.StartTime.Hour, slot.StartTime.Minute, 0, TimeSpan.Zero);
        }

        var now = DateTimeOffset.UtcNow;
        var targetDay = slot.DayOfWeek ?? DayOfWeek.Monday;
        var daysUntil = ((int)targetDay - (int)now.DayOfWeek + 7) % 7;
        if (daysUntil == 0) daysUntil = 7; // Prefer next week over today

        var targetDate = now.AddDays(daysUntil).Date;
        return new DateTimeOffset(
            targetDate.Year, targetDate.Month, targetDate.Day,
            slot.StartTime.Hour, slot.StartTime.Minute, 0, TimeSpan.Zero);
    }
}
