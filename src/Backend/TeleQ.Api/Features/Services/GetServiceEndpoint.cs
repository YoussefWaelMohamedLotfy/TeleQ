using FastEndpoints;
using FastEndpoints.AspVersioning;
using TeleQ.Api.Data;

namespace TeleQ.Api.Features.Services;

/// <summary>Returns a single service by its identifier.</summary>
public sealed class GetServiceEndpoint(AppDbContext db)
    : EndpointWithoutRequest<ServiceResponse, ServiceMapper>
{
    public override void Configure()
    {
        Get("/services/{id:guid}");
        Version(1);
        Description(x => x.WithTags("Services"));
        Options(x => x.WithVersionSet("TeleQ").MapToApiVersion(1.0));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var service = await db.Services.FindAsync([id], ct);

        if (service is null) { await Send.NotFoundAsync(ct); return; }

        await Send.OkAsync(Map.FromEntity(service), ct);
    }
}
