namespace TeleQ.Api.Data.Entities;

public sealed class Service
{
    public Guid Id { get; set; }
    public Guid BranchId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int EstimatedDurationMinutes { get; set; } = 10;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }

    public Branch Branch { get; set; } = null!;
    public ICollection<TimeSlot> TimeSlots { get; set; } = [];
    public ICollection<ClerkAssignment> ClerkAssignments { get; set; } = [];
}
