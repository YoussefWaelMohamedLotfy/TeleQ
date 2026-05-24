using TeleQ.Api.Common.Aggregates;
using TeleQ.Api.Common.DomainEvents;
using TeleQ.Api.Common.Projections;

namespace TeleQ.Tests.Projections;

/// <summary>
/// Unit tests for BranchQueueProjection Apply methods.
/// Exercises the projection logic in isolation — no Marten infrastructure needed.
/// </summary>
public sealed class BranchQueueProjectionTests
{
    private static readonly Guid _branchId = Guid.Parse("11111111-0000-0000-0000-000000000000");
    private static readonly Guid _serviceId = Guid.Parse("22222222-0000-0000-0000-000000000000");
    private static readonly BranchQueueProjection _projection = new();

    // ── TicketIssued ─────────────────────────────────────────────────────────

    [Test]
    public async Task Apply_TicketIssued_AddsToWaitingAndAdvancesNextNumber()
    {
        var doc = NewSnapshot();
        var ticketId = Guid.NewGuid();

        _projection.Apply(new TicketIssued(ticketId, "A-001", "+1555", _branchId, _serviceId, 1, DateTimeOffset.UtcNow), doc);

        await Assert.That(doc.WaitingTickets).HasCount().EqualTo(1);
        await Assert.That(doc.WaitingTickets[0].TicketId).IsEqualTo(ticketId);
        await Assert.That(doc.WaitingTickets[0].Type).IsEqualTo(TicketType.WalkIn);
        await Assert.That(doc.NextQueueNumber).IsEqualTo(2);
    }

    [Test]
    public async Task Apply_MultipleTicketsIssued_IncreasesWaitingCount()
    {
        var doc = NewSnapshot();

        _projection.Apply(new TicketIssued(Guid.NewGuid(), "A-001", "+1", _branchId, _serviceId, 1, DateTimeOffset.UtcNow), doc);
        _projection.Apply(new TicketIssued(Guid.NewGuid(), "A-002", "+2", _branchId, _serviceId, 2, DateTimeOffset.UtcNow), doc);
        _projection.Apply(new TicketIssued(Guid.NewGuid(), "A-003", "+3", _branchId, _serviceId, 3, DateTimeOffset.UtcNow), doc);

        await Assert.That(doc.WaitingTickets).HasCount().EqualTo(3);
        await Assert.That(doc.NextQueueNumber).IsEqualTo(4);
    }

    // ── AppointmentBooked ────────────────────────────────────────────────────

    [Test]
    public async Task Apply_AppointmentBooked_AddsToWaitingWithScheduledAt()
    {
        var doc = NewSnapshot();
        var ticketId = Guid.NewGuid();
        var slotId = Guid.NewGuid();
        var scheduled = DateTimeOffset.UtcNow.AddDays(1);

        _projection.Apply(new AppointmentBooked(
            ticketId, "B-001", "+1555", _branchId, _serviceId,
            slotId, scheduled, 1, DateTimeOffset.UtcNow), doc);

        await Assert.That(doc.WaitingTickets).HasCount().EqualTo(1);
        await Assert.That(doc.WaitingTickets[0].Type).IsEqualTo(TicketType.Appointment);
        await Assert.That(doc.WaitingTickets[0].ScheduledAt).IsEqualTo(scheduled);
    }

    // ── TicketCalled ─────────────────────────────────────────────────────────

    [Test]
    public async Task Apply_TicketCalled_MovesFromWaitingToCalled()
    {
        var doc = NewSnapshot();
        var ticketId = Guid.NewGuid();
        _projection.Apply(new TicketIssued(ticketId, "A-001", "+1", _branchId, _serviceId, 1, DateTimeOffset.UtcNow), doc);

        _projection.Apply(new TicketCalled(ticketId, _branchId, _serviceId, Guid.NewGuid(), "Counter 1", DateTimeOffset.UtcNow), doc);

        await Assert.That(doc.WaitingTickets).HasCount().EqualTo(0);
        await Assert.That(doc.CalledTickets).HasCount().EqualTo(1);
        await Assert.That(doc.CalledTickets[0].TicketId).IsEqualTo(ticketId);
    }

    [Test]
    public async Task Apply_TicketCalled_UnknownTicket_DoesNothing()
    {
        var doc = NewSnapshot();
        _projection.Apply(new TicketIssued(Guid.NewGuid(), "A-001", "+1", _branchId, _serviceId, 1, DateTimeOffset.UtcNow), doc);

        // Call a ticket that's not in the queue — should not throw
        _projection.Apply(new TicketCalled(Guid.NewGuid(), _branchId, _serviceId, Guid.NewGuid(), "C1", DateTimeOffset.UtcNow), doc);

        await Assert.That(doc.WaitingTickets).HasCount().EqualTo(1);
        await Assert.That(doc.CalledTickets).HasCount().EqualTo(0);
    }

    // ── TicketServed ─────────────────────────────────────────────────────────

    [Test]
    public async Task Apply_TicketServed_RemovesFromCalledAndIncrementsCounter()
    {
        var doc = NewSnapshot();
        var ticketId = Guid.NewGuid();
        var clerkId = Guid.NewGuid();
        _projection.Apply(new TicketIssued(ticketId, "A-001", "+1", _branchId, _serviceId, 1, DateTimeOffset.UtcNow), doc);
        _projection.Apply(new TicketCalled(ticketId, _branchId, _serviceId, clerkId, "C1", DateTimeOffset.UtcNow), doc);

        _projection.Apply(new TicketServed(ticketId, _branchId, _serviceId, clerkId, DateTimeOffset.UtcNow), doc);

        await Assert.That(doc.CalledTickets).HasCount().EqualTo(0);
        await Assert.That(doc.TotalServedToday).IsEqualTo(1);
    }

    // ── TicketNoShow ─────────────────────────────────────────────────────────

    [Test]
    public async Task Apply_TicketNoShow_RemovesFromCalledAndIncrementsNoShowCounter()
    {
        var doc = NewSnapshot();
        var ticketId = Guid.NewGuid();
        var clerkId = Guid.NewGuid();
        _projection.Apply(new TicketIssued(ticketId, "A-001", "+1", _branchId, _serviceId, 1, DateTimeOffset.UtcNow), doc);
        _projection.Apply(new TicketCalled(ticketId, _branchId, _serviceId, clerkId, "C1", DateTimeOffset.UtcNow), doc);

        _projection.Apply(new TicketNoShow(ticketId, _branchId, _serviceId, clerkId, DateTimeOffset.UtcNow), doc);

        await Assert.That(doc.CalledTickets).HasCount().EqualTo(0);
        await Assert.That(doc.TotalNoShowToday).IsEqualTo(1);
    }

    [Test]
    public async Task Apply_TicketNoShow_AlsoRemovesFromWaitingIfPresent()
    {
        var doc = NewSnapshot();
        var ticketId = Guid.NewGuid();
        _projection.Apply(new TicketIssued(ticketId, "A-001", "+1", _branchId, _serviceId, 1, DateTimeOffset.UtcNow), doc);

        // Mark no-show while still in waiting (edge case)
        _projection.Apply(new TicketNoShow(ticketId, _branchId, _serviceId, null, DateTimeOffset.UtcNow), doc);

        await Assert.That(doc.WaitingTickets).HasCount().EqualTo(0);
        await Assert.That(doc.TotalNoShowToday).IsEqualTo(1);
    }

    // ── TicketCancelled ──────────────────────────────────────────────────────

    [Test]
    public async Task Apply_TicketCancelled_RemovesFromWaitingAndIncrementsCancelledCounter()
    {
        var doc = NewSnapshot();
        var ticketId = Guid.NewGuid();
        _projection.Apply(new TicketIssued(ticketId, "A-001", "+1", _branchId, _serviceId, 1, DateTimeOffset.UtcNow), doc);

        _projection.Apply(new TicketCancelled(ticketId, _branchId, _serviceId, "customer", DateTimeOffset.UtcNow), doc);

        await Assert.That(doc.WaitingTickets).HasCount().EqualTo(0);
        await Assert.That(doc.TotalCancelledToday).IsEqualTo(1);
    }

    [Test]
    public async Task Apply_TicketCancelled_WhenCalledAlsoRemovesFromCalled()
    {
        var doc = NewSnapshot();
        var ticketId = Guid.NewGuid();
        var clerkId = Guid.NewGuid();
        _projection.Apply(new TicketIssued(ticketId, "A-001", "+1", _branchId, _serviceId, 1, DateTimeOffset.UtcNow), doc);
        _projection.Apply(new TicketCalled(ticketId, _branchId, _serviceId, clerkId, "C1", DateTimeOffset.UtcNow), doc);

        _projection.Apply(new TicketCancelled(ticketId, _branchId, _serviceId, "admin", DateTimeOffset.UtcNow), doc);

        await Assert.That(doc.CalledTickets).HasCount().EqualTo(0);
        await Assert.That(doc.TotalCancelledToday).IsEqualTo(1);
    }

    // ── TicketRescheduled ────────────────────────────────────────────────────

    [Test]
    public async Task Apply_TicketRescheduled_UpdatesScheduledAtInWaiting()
    {
        var doc = NewSnapshot();
        var ticketId = Guid.NewGuid();
        var oldSlotId = Guid.NewGuid();
        var newSlotId = Guid.NewGuid();
        var newTime = DateTimeOffset.UtcNow.AddDays(5);

        _projection.Apply(new AppointmentBooked(
            ticketId, "B-001", "+1", _branchId, _serviceId,
            oldSlotId, DateTimeOffset.UtcNow.AddDays(1), 1, DateTimeOffset.UtcNow), doc);

        _projection.Apply(new TicketRescheduled(
            ticketId, _branchId, _serviceId,
            oldSlotId, newSlotId, newTime, DateTimeOffset.UtcNow), doc);

        await Assert.That(doc.WaitingTickets[0].ScheduledAt).IsEqualTo(newTime);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private BranchQueueSnapshot NewSnapshot() =>
        new() { Id = $"{_branchId}:{_serviceId}", BranchId = _branchId, ServiceId = _serviceId };
}
