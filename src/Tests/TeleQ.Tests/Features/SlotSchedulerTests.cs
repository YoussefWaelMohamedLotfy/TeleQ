using TeleQ.Api.Data.Entities;
using TeleQ.Api.Features.Tickets;

namespace TeleQ.Tests.Features;

/// <summary>
/// Unit tests for SlotScheduler.NextOccurrence — date/time logic for time slots.
/// </summary>
public sealed class SlotSchedulerTests
{
    // ── One-off (non-recurring) slots ─────────────────────────────────────────

    [Test]
    public async Task OneOff_ReturnsExactDateAndTime()
    {
        var slot = new TimeSlot
        {
            StartTime = new TimeOnly(10, 30),
            EndTime = new TimeOnly(11, 0),
            IsRecurring = false,
            Date = new DateOnly(2025, 12, 25)
        };

        var result = SlotScheduler.NextOccurrence(slot);

        await Assert.That(result.Year).IsEqualTo(2025);
        await Assert.That(result.Month).IsEqualTo(12);
        await Assert.That(result.Day).IsEqualTo(25);
        await Assert.That(result.Hour).IsEqualTo(10);
        await Assert.That(result.Minute).IsEqualTo(30);
        await Assert.That(result.Offset).IsEqualTo(TimeSpan.Zero);
    }

    [Test]
    public async Task OneOff_WithNullDate_FallsBackToRecurringLogic()
    {
        // IsRecurring=false but Date=null means there's no specific date — should not throw
        var slot = new TimeSlot
        {
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(9, 30),
            IsRecurring = false,
            Date = null,
            DayOfWeek = DayOfWeek.Monday
        };

        // Should not throw; falls back to recurring logic
        var result = SlotScheduler.NextOccurrence(slot);

        await Assert.That(result > DateTimeOffset.UtcNow).IsTrue();
    }

    // ── Recurring slots ───────────────────────────────────────────────────────

    [Test]
    public async Task Recurring_ResultIsAlwaysInFuture()
    {
        var slot = new TimeSlot
        {
            StartTime = new TimeOnly(14, 0),
            EndTime = new TimeOnly(15, 0),
            IsRecurring = true,
            DayOfWeek = DayOfWeek.Friday
        };

        var result = SlotScheduler.NextOccurrence(slot);

        await Assert.That(result > DateTimeOffset.UtcNow).IsTrue();
    }

    [Test]
    [Arguments(DayOfWeek.Monday)]
    [Arguments(DayOfWeek.Tuesday)]
    [Arguments(DayOfWeek.Wednesday)]
    [Arguments(DayOfWeek.Thursday)]
    [Arguments(DayOfWeek.Friday)]
    [Arguments(DayOfWeek.Saturday)]
    [Arguments(DayOfWeek.Sunday)]
    public async Task Recurring_ResultMatchesRequestedDayOfWeek(DayOfWeek targetDay)
    {
        var slot = new TimeSlot
        {
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(9, 0),
            IsRecurring = true,
            DayOfWeek = targetDay
        };

        var result = SlotScheduler.NextOccurrence(slot);

        await Assert.That(result.DayOfWeek).IsEqualTo(targetDay);
    }

    [Test]
    public async Task Recurring_ResultTimeMatchesSlotStartTime()
    {
        var slot = new TimeSlot
        {
            StartTime = new TimeOnly(16, 45),
            EndTime = new TimeOnly(17, 0),
            IsRecurring = true,
            DayOfWeek = DayOfWeek.Wednesday
        };

        var result = SlotScheduler.NextOccurrence(slot);

        await Assert.That(result.Hour).IsEqualTo(16);
        await Assert.That(result.Minute).IsEqualTo(45);
    }

    [Test]
    public async Task Recurring_NeverReturnsToday_AlwaysNextWeekOrLater()
    {
        // Even if today is the target day, it should return next occurrence (not same day)
        var todayDow = DateTimeOffset.UtcNow.DayOfWeek;
        var slot = new TimeSlot
        {
            StartTime = new TimeOnly(23, 59),
            EndTime = new TimeOnly(23, 59),
            IsRecurring = true,
            DayOfWeek = todayDow
        };

        var result = SlotScheduler.NextOccurrence(slot);

        // The result should be at least tomorrow (SlotScheduler skips same-day occurrences)
        await Assert.That(result.Date > DateTimeOffset.UtcNow.Date).IsTrue();
    }
}
