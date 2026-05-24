using FastEndpoints;
using FastEndpoints.AspVersioning;
using Microsoft.Extensions.Caching.Hybrid;
using TeleQ.Api.Common;
using TeleQ.Api.Data;

namespace TeleQ.Api.Features.Branches;

/// <summary>Updates an existing branch's details. Restricted to Admin users.</summary>
public sealed class UpdateBranchEndpoint(AppDbContext db, HybridCache cache) : Endpoint<UpdateBranchRequest>
{
    public override void Configure()
    {
        Put("/branches/{id:guid}");
        Version(1);
        Policies("AdminOnly");
        Description(x => x.WithTags("Branches"));
        Options(x => x.WithVersionSet("TeleQ").MapToApiVersion(1.0));
    }

    public override async Task HandleAsync(UpdateBranchRequest req, CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var branch = await db.Branches.FindAsync([id], ct);

        if (branch is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        branch.Name = req.Name;
        branch.Address = req.Address;
        branch.PhoneNumber = req.PhoneNumber;

        await db.SaveChangesAsync(ct);
        await cache.RemoveByTagAsync(["branches", $"branch:{id}"], ct);
        await Send.NoContentAsync(ct);
    }
}
