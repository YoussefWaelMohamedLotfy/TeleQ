using TeleQ.Api.Common.DomainEvents;

namespace TeleQ.Api.Common.Aggregates;

public enum TicketType { WalkIn, Appointment }

public enum TicketStatus { Waiting, Called, Served, NoShow, Cancelled }

/// <summary>
/// Event-sourced aggregate representing a customer's queue ticket.
/// Rebuilt by Marten by replaying events from the ticket's stream.
/// </summary>
public sealed class Ticket
{
    public Guid Id { get; private set; }
    public string TicketNumber { get; private set; } = string.Empty;
    public TicketType Type { get; private set; }
    public TicketStatus Status { get; private set; }
    public string CustomerPhone { get; private set; } = string.Empty;
    public Guid BranchId { get; private set; }
    public Guid ServiceId { get; private set; }
    public Guid? TimeSlotId { get; private set; }
    public DateTimeOffset? ScheduledAt { get; private set; }
    public DateTimeOffset IssuedAt { get; private set; }
    public Guid? AssignedClerkId { get; private set; }
    public int QueuePosition { get; private set; }
    public string? CounterLabel { get; private set; }

    public void Apply(TicketIssued evt)
    {
        Id = evt.TicketId;
        TicketNumber = evt.TicketNumber;
        Type = TicketType.WalkIn;
        Status = TicketStatus.Waiting;
        CustomerPhone = evt.CustomerPhone;
        BranchId = evt.BranchId;
        ServiceId = evt.ServiceId;
        IssuedAt = evt.IssuedAt;
        QueuePosition = evt.QueuePosition;
    }

    public void Apply(AppointmentBooked evt)
    {
        Id = evt.TicketId;
        TicketNumber = evt.TicketNumber;
        Type = TicketType.Appointment;
        Status = TicketStatus.Waiting;
        CustomerPhone = evt.CustomerPhone;
        BranchId = evt.BranchId;
        ServiceId = evt.ServiceId;
        TimeSlotId = evt.TimeSlotId;
        ScheduledAt = evt.ScheduledAt;
        IssuedAt = evt.BookedAt;
        QueuePosition = evt.QueuePosition;
    }

    public void Apply(TicketCalled evt)
    {
        Status = TicketStatus.Called;
        AssignedClerkId = evt.ClerkId;
        CounterLabel = evt.CounterLabel;
    }

    public void Apply(TicketServed _) => Status = TicketStatus.Served;

    public void Apply(TicketNoShow _) => Status = TicketStatus.NoShow;

    public void Apply(TicketCancelled _) => Status = TicketStatus.Cancelled;

    public void Apply(TicketRescheduled evt)
    {
        TimeSlotId = evt.NewTimeSlotId;
        ScheduledAt = evt.NewScheduledAt;
    }
}
