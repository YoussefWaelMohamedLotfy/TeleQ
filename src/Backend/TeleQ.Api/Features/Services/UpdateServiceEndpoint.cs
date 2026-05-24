using FastEndpoints;
using FastEndpoints.AspVersioning;
using Microsoft.Extensions.Caching.Hybrid;
using TeleQ.Api.Common;
using TeleQ.Api.Data;

namespace TeleQ.Api.Features.Services;

/// <summary>Updates an existing service's details. Restricted to Admin users.</summary>
public sealed class UpdateServiceEndpoint(AppDbContext db, HybridCache cache) : Endpoint<UpdateServiceRequest>
{
    public override void Configure()
    {
        Put("/services/{id:guid}");
        Version(1);
        Policies("AdminOnly");
        Description(x => x.WithTags("Services"));
        Options(x => x.WithVersionSet("TeleQ").MapToApiVersion(1.0));
    }

    public override async Task HandleAsync(UpdateServiceRequest req, CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var service = await db.Services.FindAsync([id], ct);

        if (service is null) { await Send.NotFoundAsync(ct); return; }

        var branchId = service.BranchId;
        service.Name = req.Name;
        service.Description = req.Description;
        service.EstimatedDurationMinutes = req.EstimatedDurationMinutes;

        await db.SaveChangesAsync(ct);
        await cache.RemoveByTagAsync(["services", $"services:branch:{branchId}", $"service:{id}"], ct);
        await Send.NoContentAsync(ct);
    }
}
