using FastEndpoints;
using FastEndpoints.AspVersioning;
using TeleQ.Api.Data;

namespace TeleQ.Api.Features.Branches;

/// <summary>Returns a single branch by its identifier.</summary>
public sealed class GetBranchEndpoint(AppDbContext db)
    : EndpointWithoutRequest<BranchResponse, BranchMapper>
{
    public override void Configure()
    {
        Get("/branches/{id:guid}");
        Version(1);
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

        await Send.OkAsync(Map.FromEntity(branch), ct);
    }
}
