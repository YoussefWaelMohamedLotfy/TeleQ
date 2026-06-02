namespace TeleQ.Messaging.Shared.DomainEvents;

// Walk-in ticket created at the counter or via bot
public record TicketIssued(
    Guid TicketId,
    string TicketNumber,
    string CustomerPhone,
    Guid BranchId,
    Guid ServiceId,
    int QueuePosition,
    DateTimeOffset IssuedAt);

// Appointment ticket booked for a future time slot
public record AppointmentBooked(
    Guid TicketId,
    string TicketNumber,
    string CustomerPhone,
    Guid BranchId,
    Guid ServiceId,
    Guid TimeSlotId,
    DateTimeOffset ScheduledAt,
    int QueuePosition,
    DateTimeOffset BookedAt);

// Clerk called the customer to the counter
public record TicketCalled(
    Guid TicketId,
    Guid BranchId,
    Guid ServiceId,
    Guid ClerkId,
    string CounterLabel,
    DateTimeOffset CalledAt);

// Clerk marked the ticket as served
public record TicketServed(
    Guid TicketId,
    Guid BranchId,
    Guid ServiceId,
    Guid ClerkId,
    DateTimeOffset ServedAt);

// Customer did not show up
public record TicketNoShow(
    Guid TicketId,
    Guid BranchId,
    Guid ServiceId,
    Guid? ClerkId,
    DateTimeOffset MarkedAt);

// Ticket was cancelled by customer or admin
public record TicketCancelled(
    Guid TicketId,
    Guid BranchId,
    Guid ServiceId,
    string CancelledBy,
    DateTimeOffset CancelledAt);

// Customer rescheduled their appointment to a different time slot
public record TicketRescheduled(
    Guid TicketId,
    Guid BranchId,
    Guid ServiceId,
    Guid OldTimeSlotId,
    Guid NewTimeSlotId,
    DateTimeOffset NewScheduledAt,
    DateTimeOffset RescheduledAt);
