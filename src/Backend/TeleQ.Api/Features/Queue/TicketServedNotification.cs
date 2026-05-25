using Mediator;

namespace TeleQ.Api.Features.Queue;

/// <summary>Published after a ticket is marked as served. Triggers a Telegram thank-you message to the customer.</summary>
public sealed record TicketServedNotification(
    string CustomerPhone,
    string TicketNumber) : INotification;
