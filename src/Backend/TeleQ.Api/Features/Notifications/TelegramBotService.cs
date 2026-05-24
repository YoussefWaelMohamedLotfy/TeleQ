using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;

namespace TeleQ.Api.Features.Notifications;

/// <summary>
/// Hosted service that starts the Telegram Bot in either webhook or long-polling mode.
/// All update-processing logic lives in <see cref="TelegramUpdateHandler"/>.
/// </summary>
public sealed class TelegramBotService(
    IOptions<TelegramBotOptions> options,
    ILogger<TelegramBotService> logger,
    ITelegramBotClient botClient,
    TelegramUpdateHandler handler) : BackgroundService
{
    private readonly TelegramBotOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.BotToken))
        {
            logger.LogWarning("Telegram Bot is disabled or BotToken is not configured.");
            return;
        }

        var me = await botClient.GetMe(stoppingToken);
        logger.LogInformation("Telegram Bot started: @{Username}", me.Username);

        if (!string.IsNullOrWhiteSpace(_options.WebhookUrl))
        {
            await botClient.SetWebhook(
                url: _options.WebhookUrl,
                secretToken: _options.WebhookSecretToken,
                allowedUpdates: [UpdateType.Message, UpdateType.CallbackQuery],
                cancellationToken: stoppingToken);

            logger.LogInformation("Telegram webhook registered at {Url}", _options.WebhookUrl);

            // Telegram pushes updates to the webhook endpoint; nothing else to do here.
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
        else
        {
            // Delete any previously registered webhook so long-polling works correctly.
            await botClient.DeleteWebhook(cancellationToken: stoppingToken);

            logger.LogInformation("Telegram Bot running in long-polling mode.");

            await botClient.ReceiveAsync(
                updateHandler: handler,
                receiverOptions: new ReceiverOptions
                {
                    AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery]
                },
                cancellationToken: stoppingToken);
        }
    }
}

public sealed record ChatContext
{
    public ConversationStep Step { get; init; } = ConversationStep.Idle;
    public Guid? SelectedBranchId { get; init; }
    public Guid? SelectedServiceId { get; init; }
    public Guid? SelectedTimeSlotId { get; init; }
    public string? PendingCommand { get; init; }
}

public enum ConversationStep
{
    Idle,
    AwaitingBranchSelection,
    AwaitingServiceSelection,
    AwaitingSlotSelection,
    AwaitingPhoneNumber,
}
