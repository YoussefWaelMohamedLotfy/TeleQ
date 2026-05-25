using Mediator;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using TeleQ.Api.Data;
using TeleQ.Api.Features.Queue;

namespace TeleQ.Api.Features.Notifications;

/// <summary>
/// Sends a Telegram thank-you message to the customer when their ticket is marked as served.
/// Resolved from the DI container; uses its own scoped DbContext so it is independent of the
/// originating HTTP request's lifetime.
/// </summary>
public sealed class TicketServedNotificationHandler(
    IServiceScopeFactory scopeFactory,
    ITelegramBotClient botClient,
    ILogger<TicketServedNotificationHandler> logger) : INotificationHandler<TicketServedNotification>
{
    public async ValueTask Handle(TicketServedNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            // Create a fresh scope so the DbContext lifetime is independent of the HTTP request.
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var customer = await EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
                db.TelegramCustomers.AsNoTracking(),
                c => c.PhoneNumber == notification.CustomerPhone,
                CancellationToken.None);

            if (customer is null)
                return;

            await botClient.SendMessage(
                customer.TelegramChatId,
                $"✅ Ticket {notification.TicketNumber} has been served. We hope your visit was convenient and we look forward to seeing you again! 😊",
                cancellationToken: CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send served Telegram notification to {Phone}", notification.CustomerPhone);
        }
    }
}
