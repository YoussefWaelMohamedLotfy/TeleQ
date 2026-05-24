namespace TeleQ.Api.Features.Branches;

public sealed record BranchResponse(
    Guid Id,
    string Name,
    string Address,
    string? PhoneNumber,
    bool IsActive,
    DateTimeOffset CreatedAt);

public sealed record CreateBranchRequest(string Name, string Address, string? PhoneNumber);

public sealed record UpdateBranchRequest(string Name, string Address, string? PhoneNumber);
