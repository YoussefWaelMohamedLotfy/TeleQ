using FastEndpoints;
using FastEndpoints.AspVersioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using TeleQ.Api.Common;
using TeleQ.Api.Data;

namespace TeleQ.Api.Features.Branches;

/// <summary>Returns all active branches. Restricted to Admin users.</summary>
public sealed class GetBranchesEndpoint(AppDbContext db, BranchMapper mapper, HybridCache cache)
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
        var result = await cache.GetOrCreateAsync(
            CacheKeys.BranchList(),
            async ct =>
            {
                var branches = await db.Branches
                    .Where(b => b.IsActive)
                    .OrderBy(b => b.Name)
                    .ToListAsync(ct);
                return branches.Select(mapper.FromEntity).ToList();
            },
            CacheOptions.Static,
            CacheKeys.BranchListTags(),
            ct);

        await Send.OkAsync(result, ct);
    }
}
