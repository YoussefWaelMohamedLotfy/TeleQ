using FastEndpoints;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace TeleQ.Api.Features.Notifications;

/// <summary>
/// Receives Telegram webhook POST requests at <c>POST /bot/telegram</c>.
/// ASP.NET Core / FastEndpoints binds the <see cref="Update"/> object directly
/// from the request body (using the Telegram.Bot JSON converters registered in
/// <c>Program.cs</c>), matching the pattern from the official Telegram.Bot examples.
/// </summary>
public sealed class TelegramWebhookEndpoint(
    TelegramUpdateHandler handler,
    ITelegramBotClient botClient,
    IOptions<TelegramBotOptions> options) : Endpoint<Update>
{
    private const string SecretHeader = "X-Telegram-Bot-Api-Secret-Token";

    public override void Configure()
    {
        Post("/bot/telegram");
        AllowAnonymous();
        Options(x => x.AllowAnonymous().ExcludeFromDescription());
    }

    public override async Task HandleAsync(Update req, CancellationToken ct)
    {
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

        // Fire-and-forget: return 200 OK immediately so Telegram won't retry.
        _ = Task.Run(() => handler.HandleUpdateAsync(botClient, req, CancellationToken.None), ct);
        await Send.OkAsync(ct);
    }
}
