using FastEndpoints;
using FastEndpoints.AspVersioning;
using TeleQ.Api.Data;

namespace TeleQ.Api.Features.Branches;

/// <summary>Creates a new branch. Restricted to Admin users.</summary>
public sealed class CreateBranchEndpoint(AppDbContext db)
    : Endpoint<CreateBranchRequest, BranchResponse, BranchMapper>
{
    public override void Configure()
    {
        Post("/branches");
        Version(1);
        Policies("AdminOnly");
        Description(x => x.WithTags("Branches"));
        Options(x => x.WithVersionSet("TeleQ").MapToApiVersion(1.0));
    }

    public override async Task HandleAsync(CreateBranchRequest req, CancellationToken ct)
    {
        var branch = Map.ToEntity(req);
        db.Branches.Add(branch);
        await db.SaveChangesAsync(ct);

        await Send.CreatedAtAsync<GetBranchEndpoint>(
            new { id = branch.Id },
            Map.FromEntity(branch),
            cancellation: ct);
    }
}
