namespace TeleQ.Api.Data.Entities;

/// <summary>
/// Maps a Keycloak clerk user (by subject claim) to a branch and service.
/// A clerk can be assigned to multiple services at a branch.
/// </summary>
public sealed class ClerkAssignment
{
    public Guid Id { get; set; }

    /// <summary>Keycloak subject claim (sub) of the clerk.</summary>
    public string ClerkId { get; set; } = string.Empty;

    public string ClerkDisplayName { get; set; } = string.Empty;
    public string CounterLabel { get; set; } = string.Empty;

    public Guid BranchId { get; set; }
    public Guid ServiceId { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTimeOffset AssignedAt { get; set; }

    public Branch Branch { get; set; } = null!;
    public Service Service { get; set; } = null!;
}
