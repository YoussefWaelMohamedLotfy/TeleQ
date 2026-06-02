namespace TeleQ.Messaging.Worker.Contracts;

/// <summary>
/// MassTransit message contract: sent by TeleQ.Api, consumed by TeleQ.Messaging.Worker
/// to deliver a Telegram message to a specific chat.
/// </summary>
public sealed record SendTelegramMessage(long ChatId, string Text);
