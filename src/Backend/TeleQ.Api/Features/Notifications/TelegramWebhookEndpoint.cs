using FastEndpoints;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace TeleQ.Api.Features.Notifications;

/// <summary>
/// Receives Telegram webhook POST requests at <c>POST /bot/telegram</c>.
/// Telegram must be configured to deliver updates to this URL via
/// <c>setWebhook</c>, which <see cref="TelegramBotService"/> handles on startup
/// when <see cref="TelegramBotOptions.WebhookUrl"/> is set.
/// </summary>
public sealed class TelegramWebhookEndpoint(
    TelegramUpdateHandler handler,
    ITelegramBotClient botClient,
    IOptions<TelegramBotOptions> options) : EndpointWithoutRequest
{
    private const string SecretHeader = "X-Telegram-Bot-Api-Secret-Token";

    public override void Configure()
    {
        Post("/bot/telegram");
        AllowAnonymous();
        // Keep this endpoint out of the OpenAPI docs — it is Telegram infrastructure.
        Options(x => x.ExcludeFromDescription());
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        // Reject requests that do not carry the expected secret token.
        var secret = options.Value.WebhookSecretToken;
        if (!string.IsNullOrWhiteSpace(secret))
        {
            var incoming = HttpContext.Request.Headers[SecretHeader].FirstOrDefault();
            if (!string.Equals(incoming, secret, StringComparison.Ordinal))
            {
                await Send.UnauthorizedAsync(ct);
                return;
            }
        }

        Update? update;
        try
        {
            update = await HttpContext.Request.ReadFromJsonAsync<Update>(
                JsonBotAPI.Options, ct);
        }
        catch
        {
            AddError("Invalid update payload.");
            await Send.ErrorsAsync(400, ct);
            return;
        }

        if (update is null)
        {
            AddError("Empty update payload.");
            await Send.ErrorsAsync(400, ct);
            return;
        }

        // Fire-and-forget so Telegram gets its 200 OK immediately and won't retry.
        _ = Task.Run(() => handler.HandleUpdateAsync(botClient, update, CancellationToken.None), ct);

        await Send.OkAsync(ct);
    }
}
