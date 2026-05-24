using FastEndpoints;
using FastEndpoints.AspVersioning;
using TeleQ.Api.Data;

namespace TeleQ.Api.Features.Services;

/// <summary>Soft-deletes (deactivates) a service. Restricted to Admin users.</summary>
public sealed class DeactivateServiceEndpoint(AppDbContext db) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/services/{id:guid}");
        Version(1);
        Policies("AdminOnly");
        Description(x => x.WithTags("Services"));
        Options(x => x.WithVersionSet("TeleQ").MapToApiVersion(1.0));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var service = await db.Services.FindAsync([id], ct);

        if (service is null) { await Send.NotFoundAsync(ct); return; }

        service.IsActive = false;
        await db.SaveChangesAsync(ct);
        await Send.NoContentAsync(ct);
    }
}
