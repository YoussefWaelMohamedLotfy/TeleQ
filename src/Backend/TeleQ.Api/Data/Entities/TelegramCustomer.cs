/// <summary>
/// Stores the Telegram chat ID associated with a customer phone number,
/// enabling proactive push notifications when their ticket is called.
/// </summary>
public sealed class TelegramCustomer
{
    public Guid Id { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public long TelegramChatId { get; set; }
    public DateTimeOffset RegisteredAt { get; set; }
    public DateTimeOffset LastActiveAt { get; set; }
}
