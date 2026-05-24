namespace TeleQ.Api.Features.Services;

public sealed record ServiceResponse(
    Guid Id,
    Guid BranchId,
    string Name,
    string? Description,
    int EstimatedDurationMinutes,
    bool IsActive,
    DateTimeOffset CreatedAt);

public sealed record CreateServiceRequest(
    string Name,
    string? Description,
    int EstimatedDurationMinutes);

public sealed record UpdateServiceRequest(
    string Name,
    string? Description,
    int EstimatedDurationMinutes);
