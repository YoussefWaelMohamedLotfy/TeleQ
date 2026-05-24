using FastEndpoints;
using TeleQ.Api.Data.Entities;

namespace TeleQ.Api.Features.Services;

/// <summary>Maps between <see cref="CreateServiceRequest"/> and <see cref="Service"/> entities, and from <see cref="Service"/> to <see cref="ServiceResponse"/>.</summary>
public sealed class ServiceMapper : Mapper<CreateServiceRequest, ServiceResponse, Service>
{
    public override Service ToEntity(CreateServiceRequest req) => new()
    {
        Id = Guid.NewGuid(),
        Name = req.Name,
        Description = req.Description,
        EstimatedDurationMinutes = req.EstimatedDurationMinutes,
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow
        // BranchId is set from the route parameter in the endpoint
    };

    public override ServiceResponse FromEntity(Service s) =>
        new(s.Id, s.BranchId, s.Name, s.Description, s.EstimatedDurationMinutes, s.IsActive, s.CreatedAt);
}
