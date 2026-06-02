namespace TeleQ.Messaging.Worker.Telegram;

/// <summary>
/// Flat projection of a <c>Service</c> + its parent <c>Branch</c> stored in HybridCache.
/// Navigation properties are intentionally absent to avoid circular-reference serialization errors.
/// </summary>
internal sealed record CachedServiceInfo(
    Guid Id,
    Guid BranchId,
    string Name,
    string BranchName);
