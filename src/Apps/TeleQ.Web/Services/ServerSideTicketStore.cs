using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Hybrid;

namespace TeleQ.Web.Services;

/// <summary>
/// Stores the full authentication ticket (including Keycloak tokens) in server-side
/// HybridCache (L1 in-memory + optional L2 distributed). Only a small GUID key is
/// written to the browser cookie, keeping it under 200 bytes and preventing HTTP 431.
/// Add an IDistributedCache (e.g. Redis) before AddHybridCache() to enable L2 storage
/// and support multi-node deployments automatically.
/// </summary>
public sealed class ServerSideTicketStore(HybridCache cache) : ITicketStore
{
    private const string KeyPrefix = "auth-ticket:";

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var key = KeyPrefix + Guid.NewGuid().ToString("N");
        await RenewAsync(key, ticket);
        return key;
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        var expiry = ticket.Properties.ExpiresUtc;
        var options = new HybridCacheEntryOptions
        {
            // L2 / absolute expiry — honour the ticket's own lifetime, fall back to 8 h
            Expiration = expiry.HasValue
                ? expiry.Value - DateTimeOffset.UtcNow
                : TimeSpan.FromHours(8),
            // L1 / local memory expiry — slide active sessions, must be ≤ Expiration
            LocalCacheExpiration = TimeSpan.FromMinutes(30),
        };

        return cache.SetAsync(key, TicketSerializer.Default.Serialize(ticket), options)
                    .AsTask();
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        // GetOrCreateAsync returns a sentinel (empty array) when the key is absent so we
        // avoid null-value caching edge-cases. Empty result == expired/removed session.
        var bytes = await cache.GetOrCreateAsync<byte[]>(
            key,
            _ => ValueTask.FromResult(Array.Empty<byte>()));

        return bytes.Length > 0 ? TicketSerializer.Default.Deserialize(bytes) : null;
    }

    public Task RemoveAsync(string key) =>
        cache.RemoveAsync(key).AsTask();
}
