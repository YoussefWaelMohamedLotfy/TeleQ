using FastEndpoints;
using TeleQ.Api.Data.Entities;

namespace TeleQ.Api.Features.Branches;

/// <summary>Maps between <see cref="CreateBranchRequest"/> and <see cref="Branch"/> entities, and from <see cref="Branch"/> to <see cref="BranchResponse"/>.</summary>
public sealed class BranchMapper : Mapper<CreateBranchRequest, BranchResponse, Branch>
{
    public override Branch ToEntity(CreateBranchRequest req) => new()
    {
        Id = Guid.NewGuid(),
        Name = req.Name,
        Address = req.Address,
        PhoneNumber = req.PhoneNumber,
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow
    };

    public override BranchResponse FromEntity(Branch b) =>
        new(b.Id, b.Name, b.Address, b.PhoneNumber, b.IsActive, b.CreatedAt);
}
