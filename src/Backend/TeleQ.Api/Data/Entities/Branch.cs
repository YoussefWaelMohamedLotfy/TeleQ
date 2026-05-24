namespace TeleQ.Api.Data.Entities;

public sealed class Branch
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<Service> Services { get; set; } = [];
    public ICollection<ClerkAssignment> ClerkAssignments { get; set; } = [];
}
