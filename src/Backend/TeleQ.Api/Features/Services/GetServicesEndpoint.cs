using FastEndpoints;
using FastEndpoints.AspVersioning;
using Microsoft.EntityFrameworkCore;
using TeleQ.Api.Data;

namespace TeleQ.Api.Features.Services;

/// <summary>Returns all active services for a branch, ordered by name.</summary>
public sealed class GetServicesEndpoint(AppDbContext db, ServiceMapper mapper)
    : EndpointWithoutRequest<List<ServiceResponse>>
{
    public override void Configure()
    {
        Get("/branches/{branchId:guid}/services");
        Version(1);
        Description(x => x.WithTags("Services"));
        Options(x => x.WithVersionSet("TeleQ").MapToApiVersion(1.0));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var branchId = Route<Guid>("branchId");

        var services = await db.Services
            .Where(s => s.BranchId == branchId && s.IsActive)
            .OrderBy(s => s.Name)
            .ToListAsync(ct);

        await Send.OkAsync(services.Select(mapper.FromEntity).ToList(), ct);
    }
}
