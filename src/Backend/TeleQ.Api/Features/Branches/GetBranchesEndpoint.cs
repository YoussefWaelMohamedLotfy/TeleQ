using FastEndpoints;
using FastEndpoints.AspVersioning;
using Microsoft.EntityFrameworkCore;
using TeleQ.Api.Data;

namespace TeleQ.Api.Features.Branches;

/// <summary>Returns all active branches ordered by name.</summary>
public sealed class GetBranchesEndpoint(AppDbContext db, BranchMapper mapper)
    : EndpointWithoutRequest<List<BranchResponse>>
{
    public override void Configure()
    {
        Get("/branches");
        Version(1);
        Policies("AdminOnly");
        Description(x => x.WithTags("Branches"));
        Options(x => x.WithVersionSet("TeleQ").MapToApiVersion(1.0));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var branches = await db.Branches
            .Where(b => b.IsActive)
            .OrderBy(b => b.Name)
            .ToListAsync(ct);

        await Send.OkAsync(branches.Select(mapper.FromEntity).ToList(), ct);
    }
}
