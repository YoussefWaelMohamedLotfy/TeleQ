using MassTransit;
using Telegram.Bot;
using TeleQ.Messaging.Worker.Contracts;

namespace TeleQ.Messaging.Worker.Consumers;

/// <summary>
/// MassTransit consumer that receives a <see cref="SendTelegramMessage"/> command
/// from TeleQ.Api (published via RabbitMQ) and delivers it to the target Telegram chat.
/// </summary>
public sealed class SendTelegramMessageConsumer(
    ITelegramBotClient botClient,
    ILogger<SendTelegramMessageConsumer> logger) : IConsumer<SendTelegramMessage>
{
    public async Task Consume(ConsumeContext<SendTelegramMessage> context)
    {
        var message = context.Message;
        try
        {
            await botClient.SendMessage(message.ChatId, message.Text, cancellationToken: context.CancellationToken);
            logger.LogDebug("Sent Telegram message to chat {ChatId}", message.ChatId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send Telegram message to chat {ChatId}", message.ChatId);
            throw;
        }
    }
}
