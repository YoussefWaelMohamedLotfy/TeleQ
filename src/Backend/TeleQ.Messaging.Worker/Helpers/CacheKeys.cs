using Microsoft.Extensions.Caching.Hybrid;

namespace TeleQ.Messaging.Worker.Helpers;

/// <summary>Provides type-safe cache key strings and tag lists for all cached resources in the Worker.</summary>
internal static class CacheKeys
{
    public static string TelegramCustomer(long chatId) => $"telegram:customer:{chatId}";
    public static string BranchListEntities() => "branches:entities";
    public static string BranchEntity(Guid id) => $"branch:{id}:entity";
    public static string ServiceListEntities(Guid branchId) => $"services:branch:{branchId}:entities";
    public static string ServiceWithBranch(Guid id) => $"service:with-branch:{id}";
    public static string TimeSlotEntity(Guid id) => $"timeslot:{id}:entity";
    public static string AvailableTimeSlots(Guid serviceId) => $"timeslots:service:{serviceId}:available";

    public static IReadOnlyList<string> TelegramCustomerTags(long chatId) => [$"telegram:customer:{chatId}"];
    public static IReadOnlyList<string> BranchListTags() => ["branches"];
    public static IReadOnlyList<string> BranchTags(Guid id) => ["branches", $"branch:{id}"];
    public static IReadOnlyList<string> ServiceListTags(Guid branchId) => ["services", $"services:branch:{branchId}"];
    public static IReadOnlyList<string> ServiceWithBranchTags(Guid id) => ["services", $"service:{id}", "branches"];
    public static IReadOnlyList<string> TimeSlotListTags(Guid serviceId) => ["timeslots", $"timeslots:service:{serviceId}"];
    public static IReadOnlyList<string> QueueTags(Guid branchId, Guid serviceId) => [$"queue:{branchId}:{serviceId}"];
}

/// <summary>Provides standard HybridCacheEntryOptions for different resource volatility levels.</summary>
internal static class CacheOptions
{
    /// <summary>For relatively static resources (branches, services, time slots): 10 min L2, 5 min L1.</summary>
    public static readonly HybridCacheEntryOptions Static = new()
    {
        Expiration = TimeSpan.FromMinutes(10),
        LocalCacheExpiration = TimeSpan.FromMinutes(5)
    };

    /// <summary>For live queue state: 30 sec L2, 15 sec L1.</summary>
    public static readonly HybridCacheEntryOptions Queue = new()
    {
        Expiration = TimeSpan.FromSeconds(30),
        LocalCacheExpiration = TimeSpan.FromSeconds(15)
    };

    /// <summary>For Telegram customer records: 5 min L2, 2 min L1.</summary>
    public static readonly HybridCacheEntryOptions Customer = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(2)
    };
}
