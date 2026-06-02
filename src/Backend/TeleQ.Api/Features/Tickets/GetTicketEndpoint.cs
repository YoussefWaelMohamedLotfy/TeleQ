using FastEndpoints;
using FastEndpoints.AspVersioning;
using Marten;
using Microsoft.Extensions.Caching.Hybrid;
using TeleQ.Api.Common;
using TeleQ.Messaging.Shared.Aggregates;

namespace TeleQ.Api.Features.Tickets;

/// <summary>Returns a ticket's current state by replaying its event stream.</summary>
public sealed class GetTicketEndpoint(IDocumentSession session, HybridCache cache)
    : EndpointWithoutRequest<TicketResponse, TicketMapper>
{
    public override void Configure()
    {
        Get("/tickets/{id:guid}");
        Version(1);
        Description(x => x.WithTags("Tickets"));
        Options(x => x.WithVersionSet("TeleQ").MapToApiVersion(1.0));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");

        var result = await cache.GetOrCreateAsync<TicketResponse?>(
            CacheKeys.Ticket(id),
            async ct =>
            {
                var ticket = await session.Events.AggregateStreamAsync<Ticket>(id, token: ct);
                return ticket is null ? null : Map.FromEntity(ticket);
            },
            CacheOptions.Ticket,
            CacheKeys.TicketTags(id),
            ct);

        if (result is null) { await Send.NotFoundAsync(ct); return; }
        await Send.OkAsync(result, ct);
    }
}
