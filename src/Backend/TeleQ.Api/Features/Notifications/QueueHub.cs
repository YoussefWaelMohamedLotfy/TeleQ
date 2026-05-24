using Microsoft.AspNetCore.SignalR;

namespace TeleQ.Api.Features.Notifications;

/// <summary>
/// SignalR hub for real-time queue updates.
/// Clients join groups keyed by branch + service to receive targeted notifications.
/// Group name format: "queue:{branchId}:{serviceId}"
/// </summary>
public sealed class QueueHub : Hub
{
    public async Task JoinQueue(Guid branchId, Guid serviceId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(branchId, serviceId));
    }

    public async Task LeaveQueue(Guid branchId, Guid serviceId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(branchId, serviceId));
    }

    public static string GroupName(Guid branchId, Guid serviceId) =>
        $"queue:{branchId}:{serviceId}";
}
