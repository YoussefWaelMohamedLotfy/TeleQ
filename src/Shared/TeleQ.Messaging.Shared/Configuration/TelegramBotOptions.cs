namespace TeleQ.Messaging.Shared.Configuration;

public sealed class TelegramBotOptions
{
    public const string SectionName = "TelegramBot";

    public string BotToken { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Base URL of the customer-facing frontend application (e.g. https://app.example.com).
    /// When set, ticket confirmation messages include a direct link to the ticket details page.
    /// Leave empty to omit the link.
    /// </summary>
    public string? FrontendBaseUrl { get; set; }

    /// <summary>
    /// ngrok management API base URL (e.g. http://localhost:4040).
    /// When set, the Worker queries ngrok to discover the public tunnel URL and registers
    /// it as the Telegram webhook on startup (webhook mode).
    /// When empty, the Worker falls back to long-polling mode.
    /// </summary>
    public string? NgrokManagementUrl { get; set; }

    /// <summary>
    /// Optional secret token sent by Telegram in the X-Telegram-Bot-Api-Secret-Token header.
    /// When set, the webhook endpoint validates the header before processing the update.
    /// </summary>
    public string? WebhookSecretToken { get; set; }
}
