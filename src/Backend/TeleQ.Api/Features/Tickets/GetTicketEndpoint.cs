using FastEndpoints;
using FastEndpoints.AspVersioning;
using Marten;
using TeleQ.Api.Common.Aggregates;

namespace TeleQ.Api.Features.Tickets;

/// <summary>Returns a ticket's current state by replaying its event stream.</summary>
public sealed class GetTicketEndpoint(IDocumentSession session)
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
        var ticket = await session.Events.AggregateStreamAsync<Ticket>(id, token: ct);

        if (ticket is null) { await Send.NotFoundAsync(ct); return; }

        await Send.OkAsync(Map.FromEntity(ticket), ct);
    }
}
