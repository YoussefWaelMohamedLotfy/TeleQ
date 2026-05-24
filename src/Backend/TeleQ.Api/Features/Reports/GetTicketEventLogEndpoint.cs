using FastEndpoints;
using FastEndpoints.AspVersioning;
using Marten;
using Microsoft.Extensions.Caching.Hybrid;
using TeleQ.Api.Common;

namespace TeleQ.Api.Features.Reports;

/// <summary>Returns the full event log for a ticket's lifecycle. Restricted to Admin users.</summary>
public sealed class GetTicketEventLogEndpoint(IDocumentSession session, HybridCache cache)
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

        var result = await cache.GetOrCreateAsync<List<TicketEventEntry>?>(
            CacheKeys.TicketEventLog(id),
            async ct =>
            {
                var events = await session.Events.FetchStreamAsync(id, token: ct);
                return !events.Any()
                    ? null
                    : events.Select(e => new TicketEventEntry(
                        e.EventTypeName,
                        e.Data,
                        e.Timestamp,
                        e.Version))
                    .ToList();
            },
            CacheOptions.Stats,
            CacheKeys.EventLogTags(id),
            ct);

        if (result is null) { await Send.NotFoundAsync(ct); return; }
        await Send.OkAsync(result, ct);
    }
}
