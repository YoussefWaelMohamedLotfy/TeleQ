using FastEndpoints;
using FastEndpoints.AspVersioning;
using Microsoft.Extensions.Caching.Hybrid;
using TeleQ.Api.Common;
using TeleQ.Api.Data;

namespace TeleQ.Api.Features.Services;

/// <summary>Returns a single service by its identifier.</summary>
public sealed class GetServiceEndpoint(AppDbContext db, HybridCache cache)
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

        var result = await cache.GetOrCreateAsync<ServiceResponse?>(
            CacheKeys.Service(id),
            async ct =>
            {
                var svc = await db.Services.FindAsync([id], ct);
                return svc is null ? null : Map.FromEntity(svc);
            },
            CacheOptions.Static,
            tags: ["services", $"service:{id}"],
            cancellationToken: ct);

        if (result is null) { await Send.NotFoundAsync(ct); return; }
        await Send.OkAsync(result, ct);
    }
}
