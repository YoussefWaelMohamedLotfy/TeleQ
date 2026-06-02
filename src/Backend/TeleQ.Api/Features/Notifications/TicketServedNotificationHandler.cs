using MassTransit;
using Mediator;
using Microsoft.EntityFrameworkCore;
using TeleQ.Api.Data;
using TeleQ.Api.Features.Queue;
using TeleQ.Messaging.Worker.Contracts;

namespace TeleQ.Api.Features.Notifications;

/// <summary>
/// Sends a Telegram thank-you message to the customer when their ticket is marked as served.
/// Publishes a <see cref="SendTelegramMessage"/> to RabbitMQ via MassTransit; the Worker
/// delivers the actual Telegram message.
/// </summary>
public sealed class TicketServedNotificationHandler(
    IServiceScopeFactory scopeFactory,
    IPublishEndpoint publishEndpoint,
    ILogger<TicketServedNotificationHandler> logger) : INotificationHandler<TicketServedNotification>
{
    public async ValueTask Handle(TicketServedNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var customer = await EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
                db.TelegramCustomers.AsNoTracking(),
                c => c.PhoneNumber == notification.CustomerPhone,
                CancellationToken.None);

            if (customer is null)
                return;

            await publishEndpoint.Publish(
                new SendTelegramMessage(
                    customer.TelegramChatId,
                    $"✅ Ticket {notification.TicketNumber} has been served. We hope your visit was convenient and we look forward to seeing you again! 😊"),
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to publish served Telegram notification for {Phone}", notification.CustomerPhone);
        }
    }
}
