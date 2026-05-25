using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;

namespace TeleQ.Api.Features.Notifications;

/// <summary>
/// Hosted service that registers the Telegram Bot in either webhook or long-polling mode
/// on startup, and cleanly unregisters it (deletes the webhook) on shutdown.
/// When <see cref="TelegramBotOptions.NgrokManagementUrl"/> is set and
/// <see cref="TelegramBotOptions.WebhookUrl"/> is empty, the service queries
/// ngrok's local management API to discover the dynamically-assigned public URL.
/// All update-processing logic lives in <see cref="TelegramUpdateHandler"/>.
/// </summary>
public sealed class TelegramBotService : IHostedService
{
    private readonly TelegramBotOptions _options;
    private readonly ILogger<TelegramBotService> _logger;
    private readonly ITelegramBotClient _botClient;
    private readonly TelegramUpdateHandler _handler;
    private CancellationTokenSource? _pollingCts;

    public TelegramBotService(
        IOptions<TelegramBotOptions> options,
        ILogger<TelegramBotService> logger,
        ITelegramBotClient botClient,
        TelegramUpdateHandler handler)
    {
        _options = options.Value;
        _logger = logger;
        _botClient = botClient;
        _handler = handler;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.BotToken))
        {
            _logger.LogWarning("Telegram Bot is disabled or BotToken is not configured.");
            return;
        }

        var me = await _botClient.GetMe(cancellationToken);
        _logger.LogInformation("Telegram Bot started: @{Username}", me.Username);

        var webhookUrl = _options.WebhookUrl;

        // Auto-discover the public URL from ngrok if no static webhook URL is configured.
        if (string.IsNullOrWhiteSpace(webhookUrl) && !string.IsNullOrWhiteSpace(_options.NgrokManagementUrl))
            webhookUrl = await DiscoverNgrokWebhookUrlAsync(_options.NgrokManagementUrl, cancellationToken);

        if (!string.IsNullOrWhiteSpace(webhookUrl))
        {
            await _botClient.SetWebhook(
                url: webhookUrl,
                secretToken: _options.WebhookSecretToken,
                allowedUpdates: [UpdateType.Message, UpdateType.CallbackQuery],
                cancellationToken: cancellationToken);

            _logger.LogInformation("Telegram webhook registered at {Url}", webhookUrl);
        }
        else
        {
            await _botClient.DeleteWebhook(cancellationToken: cancellationToken);
            _logger.LogInformation("Telegram Bot running in long-polling mode.");

            _pollingCts = new CancellationTokenSource();
            _ = _botClient.ReceiveAsync(
                updateHandler: _handler,
                receiverOptions: new ReceiverOptions
                {
                    AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery]
                },
                cancellationToken: _pollingCts.Token);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_pollingCts is not null)
        {
            await _pollingCts.CancelAsync();
            _pollingCts.Dispose();
            _pollingCts = null;
        }

        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.BotToken))
            return;

        try
        {
            await _botClient.DeleteWebhook(cancellationToken: CancellationToken.None);
            _logger.LogInformation("Telegram webhook unregistered on shutdown.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unregister Telegram webhook on shutdown.");
        }
    }

    /// <summary>
    /// Polls ngrok's local management API until a tunnel with a public HTTPS URL appears,
    /// retrying up to 30 times with increasing delays to account for tunnel establishment.
    /// Returns the full webhook URL (public URL + /bot/telegram) or <see langword="null"/>.
    /// </summary>
    private async Task<string?> DiscoverNgrokWebhookUrlAsync(string managementUrl, CancellationToken ct)
    {
        var apiUrl = $"{managementUrl.TrimEnd('/')}/api/tunnels";
        using var http = new HttpClient();

        // Give ngrok a moment to establish the tunnel after the container starts.
        await Task.Delay(3000, ct);

        for (var attempt = 1; attempt <= 30; attempt++)
        {
            try
            {
                var response = await http.GetFromJsonAsync<NgrokTunnelsResponse>(apiUrl, ct);

                var publicUrl = response?.Tunnels
                    .FirstOrDefault(t => t.Proto == "https")?.PublicUrl
                    ?? response?.Tunnels.FirstOrDefault()?.PublicUrl;

                if (!string.IsNullOrWhiteSpace(publicUrl))
                {
                    _logger.LogInformation("Discovered ngrok public URL: {Url}", publicUrl);
                    return $"{publicUrl}/bot/telegram";
                }

                _logger.LogDebug("Ngrok tunnel not ready yet (attempt {Attempt}/30), retrying...", attempt);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogDebug(ex, "Waiting for ngrok management API to be ready (attempt {Attempt}/30).", attempt);
            }

            await Task.Delay(2000, ct);
        }

        _logger.LogWarning("Could not discover ngrok public URL after 30 attempts. Falling back to long-polling.");
        return null;
    }

    private sealed record NgrokTunnelsResponse(
        [property: JsonPropertyName("tunnels")] NgrokTunnel[] Tunnels);

    private sealed record NgrokTunnel(
        [property: JsonPropertyName("public_url")] string PublicUrl,
        [property: JsonPropertyName("proto")] string Proto);
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
