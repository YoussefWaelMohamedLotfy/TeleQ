using Microsoft.Extensions.Caching.Hybrid;

namespace TeleQ.Api.Common;

/// <summary>Provides type-safe cache key strings and tag lists for all cached resources.</summary>
internal static class CacheKeys
{
    // ── API DTO cache keys ─────────────────────────────────────────────────
    public static string BranchList() => "branches:all";
    public static string Branch(Guid id) => $"branch:{id}";
    public static string ServiceList(Guid branchId) => $"services:branch:{branchId}";
    public static string Service(Guid id) => $"service:{id}";
    public static string TimeSlotList(Guid serviceId) => $"timeslots:service:{serviceId}";
    public static string TimeSlot(Guid id) => $"timeslot:{id}";
    public static string Ticket(Guid id) => $"ticket:{id}";
    public static string Queue(Guid branchId, Guid serviceId) => $"queue:{branchId}:{serviceId}";
    public static string DailyStats(DateOnly date, Guid branchId, Guid serviceId) => $"stats:{date:yyyyMMdd}:{branchId}:{serviceId}";
    public static string DailyStatsRange(DateOnly from, DateOnly to, Guid branchId, Guid serviceId) => $"stats:range:{from:yyyyMMdd}:{to:yyyyMMdd}:{branchId}:{serviceId}";
    public static string TicketEventLog(Guid id) => $"events:{id}";

    // ── Entity cache keys (Telegram handler — type-distinct from DTO caches) ─
    // Keys use an ":entity"/":entities" suffix so they coexist with DTO caches
    // but share the SAME tags — admin endpoint mutations invalidate both.
    public static string TelegramCustomer(long chatId) => $"telegram:customer:{chatId}";
    public static string BranchListEntities() => "branches:entities";
    public static string BranchEntity(Guid id) => $"branch:{id}:entity";
    public static string ServiceListEntities(Guid branchId) => $"services:branch:{branchId}:entities";
    public static string ServiceWithBranch(Guid id) => $"service:with-branch:{id}";
    public static string TimeSlotEntity(Guid id) => $"timeslot:{id}:entity";
    public static string AvailableTimeSlots(Guid serviceId) => $"timeslots:service:{serviceId}:available";

    // ── Tags ──────────────────────────────────────────────────────────────
    public static IReadOnlyList<string> BranchListTags() => ["branches"];
    public static IReadOnlyList<string> BranchTags(Guid id) => ["branches", $"branch:{id}"];
    public static IReadOnlyList<string> ServiceListTags(Guid branchId) => ["services", $"services:branch:{branchId}"];
    public static IReadOnlyList<string> ServiceTags(Guid id, Guid branchId) => ["services", $"services:branch:{branchId}", $"service:{id}"];
    public static IReadOnlyList<string> TimeSlotListTags(Guid serviceId) => ["timeslots", $"timeslots:service:{serviceId}"];
    public static IReadOnlyList<string> TimeSlotTags(Guid id, Guid serviceId) => ["timeslots", $"timeslots:service:{serviceId}", $"timeslot:{id}"];
    public static IReadOnlyList<string> TicketTags(Guid id) => ["tickets", $"ticket:{id}"];
    public static IReadOnlyList<string> QueueTags(Guid branchId, Guid serviceId) => [$"queue:{branchId}:{serviceId}"];
    public static IReadOnlyList<string> StatsTags(Guid branchId, Guid serviceId) => [$"stats:{branchId}:{serviceId}"];
    public static IReadOnlyList<string> EventLogTags(Guid id) => [$"events:{id}", $"ticket:{id}"];

    // Additional tags for entity caches
    public static IReadOnlyList<string> TelegramCustomerTags(long chatId) => [$"telegram:customer:{chatId}"];
    /// <summary>Tags for service-with-branch entity: evicted by service OR branch mutations.</summary>
    public static IReadOnlyList<string> ServiceWithBranchTags(Guid id) => ["services", $"service:{id}", "branches"];
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

    /// <summary>For event-sourced ticket aggregates: 2 min L2, 1 min L1.</summary>
    public static readonly HybridCacheEntryOptions Ticket = new()
    {
        Expiration = TimeSpan.FromMinutes(2),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };

    /// <summary>For stats and event logs: 5 min L2, 2 min L1.</summary>
    public static readonly HybridCacheEntryOptions Stats = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(2)
    };

    /// <summary>For Telegram customer records: 5 min L2, 2 min L1.</summary>
    public static readonly HybridCacheEntryOptions Customer = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(2)
    };
}
