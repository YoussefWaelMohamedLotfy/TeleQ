using FastEndpoints;
using FastEndpoints.AspVersioning;
using Microsoft.Extensions.Caching.Hybrid;
using TeleQ.Api.Common;
using TeleQ.Api.Data;

namespace TeleQ.Api.Features.Branches;

/// <summary>Returns a single branch by its unique identifier. Restricted to Admin users.</summary>
public sealed class GetBranchEndpoint(AppDbContext db, HybridCache cache)
    : EndpointWithoutRequest<BranchResponse, BranchMapper>
{
    public override void Configure()
    {
        Get("/branches/{id:guid}");
        Version(1);
        Policies("AdminOnly");
        Description(x => x.WithTags("Branches"));
        Options(x => x.WithVersionSet("TeleQ").MapToApiVersion(1.0));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");

        var result = await cache.GetOrCreateAsync<BranchResponse?>(
            CacheKeys.Branch(id),
            async ct =>
            {
                var branch = await db.Branches.FindAsync([id], ct);
                return branch is null ? null : Map.FromEntity(branch);
            },
            CacheOptions.Static,
            CacheKeys.BranchTags(id),
            ct);

        if (result is null) { await Send.NotFoundAsync(ct); return; }
        await Send.OkAsync(result, ct);
    }
}
