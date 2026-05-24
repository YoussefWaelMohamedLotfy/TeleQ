namespace TeleQ.Api.Features.Notifications;

public sealed class TelegramBotOptions
{
    public const string SectionName = "TelegramBot";

    public string BotToken { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// When set, the bot operates in webhook mode and registers this URL with Telegram.
    /// Must be a publicly reachable HTTPS URL (e.g. https://yourdomain.com/bot/telegram).
    /// Leave empty to fall back to long-polling.
    /// </summary>
    public string? WebhookUrl { get; set; }

    /// <summary>
    /// Optional secret (1–256 chars, A-Z a-z 0-9 _ -) that Telegram echoes back in the
    /// <c>X-Telegram-Bot-Api-Secret-Token</c> header so the webhook endpoint can reject
    /// spoofed requests.
    /// </summary>
    public string? WebhookSecretToken { get; set; }
}
