using System.Text.Json;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;
using TeleQ.Messaging.Shared.Configuration;

namespace TeleQ.Messaging.Worker.Telegram;

/// <summary>
/// Hosted service that runs the Telegram Bot in either webhook or long-polling mode.
/// Webhook mode is preferred: if <see cref="TelegramBotOptions.NgrokManagementUrl"/> is configured,
/// the service discovers the ngrok tunnel URL and registers it with Telegram on startup.
/// When ngrok is unavailable, it falls back to long-polling automatically.
/// </summary>
public sealed class TelegramBotService : IHostedService
{
    private readonly TelegramBotOptions _options;
    private readonly ILogger<TelegramBotService> _logger;
    private readonly ITelegramBotClient _botClient;
    private readonly TelegramUpdateHandler _handler;
    private readonly IHttpClientFactory _httpClientFactory;
    private CancellationTokenSource? _pollingCts;
    private bool _webhookMode;

    public TelegramBotService(
        IOptions<TelegramBotOptions> options,
        ILogger<TelegramBotService> logger,
        ITelegramBotClient botClient,
        TelegramUpdateHandler handler,
        IHttpClientFactory httpClientFactory)
    {
        _options = options.Value;
        _logger = logger;
        _botClient = botClient;
        _handler = handler;
        _httpClientFactory = httpClientFactory;
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

        if (!string.IsNullOrWhiteSpace(_options.NgrokManagementUrl))
        {
            var webhookUrl = await DiscoverNgrokWebhookUrlAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(webhookUrl))
            {
                await RegisterWebhookAsync(webhookUrl, cancellationToken);
                _webhookMode = true;
                return;
            }
        }

        // Fall back to long-polling
        _logger.LogInformation("Telegram Bot running in long-polling mode.");
        await _botClient.DeleteWebhook(cancellationToken: cancellationToken);

        _pollingCts = new CancellationTokenSource();
        _ = _botClient.ReceiveAsync(
            updateHandler: _handler,
            receiverOptions: new ReceiverOptions
            {
                AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery]
            },
            cancellationToken: _pollingCts.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_webhookMode)
        {
            try
            {
                await _botClient.DeleteWebhook(cancellationToken: cancellationToken);
                _logger.LogInformation("Telegram webhook deleted on shutdown.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete Telegram webhook on shutdown.");
            }

            return;
        }

        if (_pollingCts is not null)
        {
            await _pollingCts.CancelAsync();
            _pollingCts.Dispose();
            _pollingCts = null;
        }

        _logger.LogInformation("Telegram Bot polling stopped.");
    }

    private async Task<string?> DiscoverNgrokWebhookUrlAsync(CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            var managementBase = _options.NgrokManagementUrl!.TrimEnd('/');
            var response = await client.GetAsync($"{managementBase}/api/tunnels", ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            foreach (var tunnel in doc.RootElement.GetProperty("tunnels").EnumerateArray())
            {
                var proto = tunnel.GetProperty("proto").GetString();
                var publicUrl = tunnel.GetProperty("public_url").GetString();

                if (proto == "https" && !string.IsNullOrWhiteSpace(publicUrl))
                {
                    _logger.LogInformation("Discovered ngrok tunnel: {Url}", publicUrl);
                    return publicUrl;
                }
            }

            _logger.LogWarning("No HTTPS ngrok tunnel found.");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query ngrok management API at {Url}", _options.NgrokManagementUrl);
            return null;
        }
    }

    private async Task RegisterWebhookAsync(string ngrokUrl, CancellationToken ct)
    {
        var webhookEndpoint = $"{ngrokUrl.TrimEnd('/')}/bot/telegram";
        await _botClient.SetWebhook(
            url: webhookEndpoint,
            secretToken: string.IsNullOrWhiteSpace(_options.WebhookSecretToken)
                ? null
                : _options.WebhookSecretToken,
            allowedUpdates: [UpdateType.Message, UpdateType.CallbackQuery],
            cancellationToken: ct);

        _logger.LogInformation("Telegram webhook registered at {Url}", webhookEndpoint);
    }
}
