namespace TeleQ.Api.Features.Notifications;

public sealed class TelegramBotOptions
{
    public const string SectionName = "TelegramBot";

    public string BotToken { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// When set, the bot operates in webhook mode and registers this URL with Telegram.
    /// Must be a publicly reachable HTTPS URL (e.g. https://yourdomain.com/bot/telegram).
    /// Leave empty to fall back to long-polling (or ngrok auto-discovery if configured).
    /// </summary>
    public string? WebhookUrl { get; set; }

    /// <summary>
    /// Optional secret (1–256 chars, A-Z a-z 0-9 _ -) that Telegram echoes back in the
    /// <c>X-Telegram-Bot-Api-Secret-Token</c> header so the webhook endpoint can reject
    /// spoofed requests.
    /// </summary>
    public string? WebhookSecretToken { get; set; }

    /// <summary>
    /// Base URL of the ngrok management API (e.g. http://localhost:4040).
    /// When set and <see cref="WebhookUrl"/> is empty, the bot service queries
    /// <c>{NgrokManagementUrl}/api/tunnels</c> on startup to discover the
    /// dynamically-assigned public URL and uses it as the webhook URL.
    /// </summary>
    public string? NgrokManagementUrl { get; set; }

    /// <summary>
    /// Base URL of the customer-facing frontend application (e.g. https://app.example.com).
    /// When set, ticket confirmation messages sent via Telegram include a direct link to
    /// the ticket details page in the format <c>{FrontendBaseUrl}/ticket/{ticketId}</c>.
    /// Leave empty to omit the link.
    /// </summary>
    public string? FrontendBaseUrl { get; set; }
}
