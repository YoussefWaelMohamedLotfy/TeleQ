using TeleQ.Messaging.Shared.Aggregates;
using TeleQ.Messaging.Shared.DomainEvents;

namespace TeleQ.Tests.Aggregates;

/// <summary>
/// Unit tests for the Ticket event-sourced aggregate.
/// Each test rebuilds the aggregate from events without any infrastructure dependencies.
/// </summary>
public sealed class TicketAggregateTests
{
    private static readonly Guid _ticketId = Guid.NewGuid();
    private static readonly Guid _branchId = Guid.NewGuid();
    private static readonly Guid _serviceId = Guid.NewGuid();
    private static readonly Guid _clerkId = Guid.NewGuid();
    private const string Phone = "+201234567890";

    // ── Walk-in lifecycle ────────────────────────────────────────────────────

    [Test]
    public async Task WalkIn_TicketIssued_SetsInitialState()
    {
        var ticket = new Ticket();
        var evt = WalkInIssued();

        ticket.Apply(evt);

        await Assert.That(ticket.Id).IsEqualTo(_ticketId);
        await Assert.That(ticket.TicketNumber).IsEqualTo("A-001");
        await Assert.That(ticket.Type).IsEqualTo(TicketType.WalkIn);
        await Assert.That(ticket.Status).IsEqualTo(TicketStatus.Waiting);
        await Assert.That(ticket.CustomerPhone).IsEqualTo(Phone);
        await Assert.That(ticket.BranchId).IsEqualTo(_branchId);
        await Assert.That(ticket.ServiceId).IsEqualTo(_serviceId);
        await Assert.That(ticket.QueuePosition).IsEqualTo(1);
    }

    [Test]
    public async Task WalkIn_TicketCalled_TransitionsToCalledStatus()
    {
        var ticket = new Ticket();
        ticket.Apply(WalkInIssued());
        ticket.Apply(new TicketCalled(_ticketId, _branchId, _serviceId, _clerkId, "Counter 1", DateTimeOffset.UtcNow));

        await Assert.That(ticket.Status).IsEqualTo(TicketStatus.Called);
        await Assert.That(ticket.CounterLabel).IsEqualTo("Counter 1");
        await Assert.That(ticket.AssignedClerkId).IsEqualTo(_clerkId);
    }

    [Test]
    public async Task WalkIn_TicketServed_TransitionsToServedStatus()
    {
        var ticket = new Ticket();
        ticket.Apply(WalkInIssued());
        ticket.Apply(new TicketCalled(_ticketId, _branchId, _serviceId, _clerkId, "Counter 1", DateTimeOffset.UtcNow));
        ticket.Apply(new TicketServed(_ticketId, _branchId, _serviceId, _clerkId, DateTimeOffset.UtcNow));

        await Assert.That(ticket.Status).IsEqualTo(TicketStatus.Served);
    }

    [Test]
    public async Task WalkIn_TicketNoShow_TransitionsToNoShowStatus()
    {
        var ticket = new Ticket();
        ticket.Apply(WalkInIssued());
        ticket.Apply(new TicketCalled(_ticketId, _branchId, _serviceId, _clerkId, "Counter 1", DateTimeOffset.UtcNow));
        ticket.Apply(new TicketNoShow(_ticketId, _branchId, _serviceId, _clerkId, DateTimeOffset.UtcNow));

        await Assert.That(ticket.Status).IsEqualTo(TicketStatus.NoShow);
    }

    [Test]
    public async Task WalkIn_TicketCancelled_TransitionsToCancelledStatus()
    {
        var ticket = new Ticket();
        ticket.Apply(WalkInIssued());
        ticket.Apply(new TicketCancelled(_ticketId, _branchId, _serviceId, "customer", DateTimeOffset.UtcNow));

        await Assert.That(ticket.Status).IsEqualTo(TicketStatus.Cancelled);
    }

    // ── Appointment lifecycle ────────────────────────────────────────────────

    [Test]
    public async Task Appointment_AppointmentBooked_SetsAppointmentState()
    {
        var slotId = Guid.NewGuid();
        var scheduled = DateTimeOffset.UtcNow.AddDays(1);
        var ticket = new Ticket();
        ticket.Apply(new AppointmentBooked(
            _ticketId, "B-001", Phone, _branchId, _serviceId,
            slotId, scheduled, 1, DateTimeOffset.UtcNow));

        await Assert.That(ticket.Type).IsEqualTo(TicketType.Appointment);
        await Assert.That(ticket.Status).IsEqualTo(TicketStatus.Waiting);
        await Assert.That(ticket.TimeSlotId).IsEqualTo(slotId);
        await Assert.That(ticket.ScheduledAt).IsEqualTo(scheduled);
    }

    [Test]
    public async Task Appointment_TicketRescheduled_UpdatesTimeSlotAndSchedule()
    {
        var oldSlotId = Guid.NewGuid();
        var newSlotId = Guid.NewGuid();
        var newScheduled = DateTimeOffset.UtcNow.AddDays(3);
        var ticket = new Ticket();
        ticket.Apply(new AppointmentBooked(
            _ticketId, "B-001", Phone, _branchId, _serviceId,
            oldSlotId, DateTimeOffset.UtcNow.AddDays(1), 1, DateTimeOffset.UtcNow));
        ticket.Apply(new TicketRescheduled(
            _ticketId, _branchId, _serviceId,
            oldSlotId, newSlotId, newScheduled, DateTimeOffset.UtcNow));

        await Assert.That(ticket.TimeSlotId).IsEqualTo(newSlotId);
        await Assert.That(ticket.ScheduledAt).IsEqualTo(newScheduled);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static TicketIssued WalkInIssued() =>
        new(_ticketId, "A-001", Phone, _branchId, _serviceId, 1, DateTimeOffset.UtcNow);
}
