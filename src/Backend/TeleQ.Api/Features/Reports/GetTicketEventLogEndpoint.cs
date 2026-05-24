using FastEndpoints;
using FastEndpoints.AspVersioning;
using Marten;

namespace TeleQ.Api.Features.Reports;

/// <summary>Returns the full event log for a ticket's lifecycle. Restricted to Admin users.</summary>
public sealed class GetTicketEventLogEndpoint(IDocumentSession session)
    : EndpointWithoutRequest<List<TicketEventEntry>>
{
    public override void Configure()
    {
        Get("/reports/tickets/{id:guid}/events");
        Version(1);
        Policies("AdminOnly");
        Description(x => x.WithTags("Reports"));
        Options(x => x.WithVersionSet("TeleQ").MapToApiVersion(1.0));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var events = await session.Events.FetchStreamAsync(id, token: ct);

        if (!events.Any())
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var result = events
            .Select(e => new TicketEventEntry(
                e.EventTypeName,
                e.Data,
                e.Timestamp,
                e.Version))
            .ToList();

        await Send.OkAsync(result, ct);
    }
}
