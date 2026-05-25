using FastEndpoints;
using FastEndpoints.AspVersioning;
using Marten;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Telegram.Bot;
using TeleQ.Api.Common;
using TeleQ.Api.Common.Aggregates;
using TeleQ.Api.Common.DomainEvents;
using TeleQ.Api.Data;
using TeleQ.Api.Features.Notifications;

namespace TeleQ.Api.Features.Queue;

/// <summary>Marks a called ticket as served, completing the service transaction. Restricted to Clerk and Admin users.</summary>
public sealed class ServeTicketEndpoint(
    IDocumentSession session,
    IHubContext<QueueHub> hub,
    HybridCache cache,
    AppDbContext db,
    ITelegramBotClient botClient) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/queue/tickets/{id:guid}/serve");
        Version(1);
        Policies("ClerkOrAdmin");
        Description(x => x.WithTags("Queue"));
        Options(x => x.WithVersionSet("TeleQ").MapToApiVersion(1.0));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var clerkId = User.FindFirst("sub")?.Value
                      ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                      ?? Guid.NewGuid().ToString();

        var ticket = await session.Events.AggregateStreamAsync<Ticket>(id, token: ct);

        if (ticket is null) { await Send.NotFoundAsync(ct); return; }

        if (ticket.Status != TicketStatus.Called)
        {
            AddError("Only a Called ticket can be marked as Served.");
            await Send.ErrorsAsync(409, ct);
            return;
        }

        var evt = new TicketServed(
            TicketId: id,
            BranchId: ticket.BranchId,
            ServiceId: ticket.ServiceId,
            ClerkId: Guid.Parse(clerkId),
            ServedAt: DateTimeOffset.UtcNow);

        session.Events.Append(id, evt);
        await session.SaveChangesAsync(ct);
        await cache.RemoveByTagAsync([$"ticket:{id}", $"queue:{ticket.BranchId}:{ticket.ServiceId}"], ct);

        await hub.Clients.Group(QueueHub.GroupName(ticket.BranchId, ticket.ServiceId))
            .SendAsync("TicketServed", new { TicketId = id, TicketNumber = ticket.TicketNumber }, ct);

        // Send a thank-you Telegram message if the customer registered via the bot.
        // Awaited before the response so the request scope (db) is still alive.
        // Uses CancellationToken.None so request teardown cannot cancel the send.
        await SendServedNotificationAsync(ticket.CustomerPhone, ticket.TicketNumber);

        await Send.NoContentAsync(ct);
    }

    private async Task SendServedNotificationAsync(string phoneNumber, string ticketNumber)
    {
        try
        {
            var customer = await EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
                db.TelegramCustomers.AsNoTracking(),
                c => c.PhoneNumber == phoneNumber,
                CancellationToken.None);

            if (customer is null)
                return;

            await botClient.SendMessage(
                customer.TelegramChatId,
                $"✅ Ticket *{ticketNumber}* has been served\\. We hope your visit was convenient and we look forward to seeing you again\\! 😊",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.MarkdownV2,
                cancellationToken: CancellationToken.None);
        }
        catch
        {
            // Notification is best-effort — never fail the serve action because of a Telegram error.
        }
    }
}
