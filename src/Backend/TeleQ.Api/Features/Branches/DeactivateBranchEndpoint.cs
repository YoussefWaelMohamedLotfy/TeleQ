using FastEndpoints;
using FastEndpoints.AspVersioning;
using Microsoft.Extensions.Caching.Hybrid;
using TeleQ.Api.Common;
using TeleQ.Api.Data;

namespace TeleQ.Api.Features.Branches;

/// <summary>Soft-deletes (deactivates) a branch. Restricted to Admin users.</summary>
public sealed class DeactivateBranchEndpoint(AppDbContext db, HybridCache cache) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/branches/{id:guid}");
        Version(1);
        Policies("AdminOnly");
        Description(x => x.WithTags("Branches"));
        Options(x => x.WithVersionSet("TeleQ").MapToApiVersion(1.0));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var branch = await db.Branches.FindAsync([id], ct);

        if (branch is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        branch.IsActive = false;
        await db.SaveChangesAsync(ct);
        await cache.RemoveByTagAsync(["branches", $"branch:{id}"], ct);
        await Send.NoContentAsync(ct);
    }
}
