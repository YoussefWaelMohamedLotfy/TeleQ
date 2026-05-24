using System.Net;
using System.Net.Http.Json;
using TeleQ.Web.Models;

namespace TeleQ.Web.Services;

public sealed class TeleQApiClient(HttpClient http)
{
    public Uri BaseAddress => http.BaseAddress ?? new Uri("http://api");

    public async Task<List<BranchResponse>?> GetBranchesAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<List<BranchResponse>>("/v1/branches", ct);

    public async Task<BranchResponse?> CreateBranchAsync(CreateBranchRequest req, CancellationToken ct = default)
    {
        using HttpResponseMessage response = await http.PostAsJsonAsync("/v1/branches", req, ct);
        return await ReadAsync<BranchResponse>(response, ct);
    }

    public async Task UpdateBranchAsync(Guid id, UpdateBranchRequest req, CancellationToken ct = default)
    {
        using HttpResponseMessage response = await http.PutAsJsonAsync($"/v1/branches/{id}", req, ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task DeleteBranchAsync(Guid id, CancellationToken ct = default)
    {
        using HttpResponseMessage response = await http.DeleteAsync($"/v1/branches/{id}", ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task<List<ServiceResponse>?> GetServicesAsync(Guid branchId, CancellationToken ct = default) =>
        await http.GetFromJsonAsync<List<ServiceResponse>>($"/v1/branches/{branchId}/services", ct);

    public async Task<ServiceResponse?> CreateServiceAsync(Guid branchId, CreateServiceRequest req, CancellationToken ct = default)
    {
        using HttpResponseMessage response = await http.PostAsJsonAsync($"/v1/branches/{branchId}/services", req, ct);
        return await ReadAsync<ServiceResponse>(response, ct);
    }

    public async Task UpdateServiceAsync(Guid id, UpdateServiceRequest req, CancellationToken ct = default)
    {
        using HttpResponseMessage response = await http.PutAsJsonAsync($"/v1/services/{id}", req, ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task DeleteServiceAsync(Guid id, CancellationToken ct = default)
    {
        using HttpResponseMessage response = await http.DeleteAsync($"/v1/services/{id}", ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task<List<TimeSlotResponse>?> GetTimeSlotsAsync(Guid serviceId, CancellationToken ct = default)
    {
        var payload = await http.GetFromJsonAsync<List<TimeSlotApiResponse>>($"/v1/services/{serviceId}/timeslots", ct);
        return payload?.Select(MapTimeSlot).Where(slot => slot is not null).Select(slot => slot!).ToList();
    }

    public async Task<TimeSlotResponse?> CreateTimeSlotAsync(Guid serviceId, CreateTimeSlotRequest req, CancellationToken ct = default)
    {
        var payload = new
        {
            BranchId = req.BranchId ?? Guid.Empty,
            req.StartTime,
            req.EndTime,
            req.Capacity,
            req.IsRecurring,
            req.DayOfWeek,
            req.Date
        };

        using HttpResponseMessage response = await http.PostAsJsonAsync($"/v1/services/{serviceId}/timeslots", payload, ct);
        return MapTimeSlot(await ReadAsync<TimeSlotApiResponse>(response, ct));
    }

    public async Task DeleteTimeSlotAsync(Guid id, CancellationToken ct = default)
    {
        using HttpResponseMessage response = await http.DeleteAsync($"/v1/timeslots/{id}", ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task<TicketResponse?> IssueWalkInAsync(IssueWalkInRequest req, CancellationToken ct = default)
    {
        using HttpResponseMessage response = await http.PostAsJsonAsync("/v1/tickets/walkin", req, ct);
        return await ReadAsync<TicketResponse>(response, ct);
    }

    public async Task<TicketResponse?> BookAppointmentAsync(BookAppointmentRequest req, CancellationToken ct = default)
    {
        using HttpResponseMessage response = await http.PostAsJsonAsync("/v1/tickets/appointment", req, ct);
        return await ReadAsync<TicketResponse>(response, ct);
    }

    public async Task<TicketResponse?> GetTicketAsync(Guid id, CancellationToken ct = default) =>
        await http.GetFromJsonAsync<TicketResponse>($"/v1/tickets/{id}", ct);

    public async Task CancelTicketAsync(Guid id, CancelTicketRequest req, CancellationToken ct = default)
    {
        using HttpResponseMessage response = await http.PatchAsJsonAsync($"/v1/tickets/{id}/cancel", req, ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task RescheduleTicketAsync(Guid id, RescheduleTicketRequest req, CancellationToken ct = default)
    {
        using HttpResponseMessage response = await http.PatchAsJsonAsync($"/v1/tickets/{id}/reschedule", req, ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task<MyPositionResponse?> GetMyPositionAsync(Guid ticketId, CancellationToken ct = default) =>
        await http.GetFromJsonAsync<MyPositionResponse>($"/v1/queue/my-position?ticketId={ticketId}", ct);

    public async Task<QueueStateResponse?> GetQueueAsync(Guid branchId, Guid serviceId, CancellationToken ct = default)
    {
        var payload = await http.GetFromJsonAsync<QueueApiResponse>($"/v1/queue/{branchId}/{serviceId}", ct);
        return payload is null ? null : new QueueStateResponse(
            payload.WaitingTickets.Count,
            payload.CalledTickets.Count,
            payload.WaitingTickets.Count + payload.CalledTickets.Count + 1,
            payload.WaitingTickets.Select(MapQueueTicket).ToList(),
            payload.CalledTickets.Select(MapQueueTicket).ToList(),
            payload.EstimatedWaitMinutes,
            payload.TotalServedToday,
            payload.TotalNoShowToday,
            payload.TotalCancelledToday);
    }

    public async Task<TicketResponse?> CallNextAsync(CallNextRequest req, CancellationToken ct = default)
    {
        using HttpResponseMessage response = await http.PostAsJsonAsync("/v1/queue/call-next", new { req.BranchId, req.ServiceId }, ct);
        var payload = await ReadAsync<CallNextApiResponse>(response, ct);
        return payload is null
            ? null
            : new TicketResponse(
                payload.TicketId,
                payload.TicketNumber,
                "WalkIn",
                "Called",
                payload.CustomerPhone,
                req.BranchId,
                req.ServiceId,
                null,
                null,
                0,
                payload.CounterLabel,
                payload.CalledAt);
    }

    public async Task ServeTicketAsync(Guid id, ServeTicketRequest req, CancellationToken ct = default)
    {
        using HttpResponseMessage response = await http.PostAsJsonAsync($"/v1/queue/tickets/{id}/serve", new { req.ClerkId }, ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task NoShowAsync(Guid id, NoShowRequest req, CancellationToken ct = default)
    {
        using HttpResponseMessage response = await http.PostAsJsonAsync($"/v1/queue/tickets/{id}/no-show", new { req.ClerkId }, ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task<DailyStatsResponse?> GetStatsAsync(Guid branchId, Guid serviceId, CancellationToken ct = default)
    {
        var payload = await http.GetFromJsonAsync<DailyStatsApiResponse>($"/v1/reports/daily-stats?branchId={branchId}&serviceId={serviceId}", ct);
        return payload is null
            ? null
            : new DailyStatsResponse(
                payload.TotalIssued,
                payload.TotalServed,
                payload.TotalNoShow,
                payload.TotalCancelled,
                0,
                payload.TotalAppointments,
                payload.TotalWalkIns);
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        await EnsureSuccessAsync(response, ct);
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return default;
        }

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var message = await response.Content.ReadAsStringAsync(ct);
        throw new HttpRequestException(
            string.IsNullOrWhiteSpace(message) ? $"Request failed with status code {(int)response.StatusCode}." : message,
            null,
            response.StatusCode);
    }

    private static QueueTicketItem MapQueueTicket(QueueTicketApiResponse item) =>
        new(item.TicketId, item.TicketNumber, item.CustomerPhone, item.QueuePosition, null, item.ScheduledAt, item.Type, item.EstimatedWaitMinutes);

    private static TimeSlotResponse? MapTimeSlot(TimeSlotApiResponse? payload) =>
        payload is null
            ? null
            : new TimeSlotResponse(
                payload.Id,
                payload.ServiceId,
                payload.BranchId,
                payload.StartTime,
                payload.EndTime,
                payload.Capacity,
                payload.BookedCount,
                payload.IsActive,
                payload.IsRecurring,
                payload.DayOfWeek,
                payload.Date,
                payload.AvailableCount);

    private sealed record TimeSlotApiResponse(
        Guid Id,
        Guid ServiceId,
        Guid BranchId,
        TimeOnly StartTime,
        TimeOnly EndTime,
        int Capacity,
        int BookedCount,
        int AvailableCount,
        bool IsRecurring,
        DayOfWeek? DayOfWeek,
        DateOnly? Date,
        bool IsActive);

    private sealed record QueueApiResponse(
        Guid BranchId,
        Guid ServiceId,
        List<QueueTicketApiResponse> WaitingTickets,
        List<QueueTicketApiResponse> CalledTickets,
        int TotalServedToday,
        int TotalNoShowToday,
        int TotalCancelledToday,
        int EstimatedWaitMinutes);

    private sealed record QueueTicketApiResponse(
        Guid TicketId,
        string TicketNumber,
        string CustomerPhone,
        int QueuePosition,
        DateTimeOffset IssuedAt,
        DateTimeOffset? ScheduledAt,
        string Type,
        int EstimatedWaitMinutes);

    private sealed record CallNextApiResponse(
        Guid TicketId,
        string TicketNumber,
        string CustomerPhone,
        string CounterLabel,
        DateTimeOffset CalledAt);

    private sealed record DailyStatsApiResponse(
        string Id,
        DateOnly Date,
        Guid BranchId,
        Guid ServiceId,
        int TotalIssued,
        int TotalServed,
        int TotalNoShow,
        int TotalCancelled,
        int TotalAppointments,
        int TotalWalkIns);
}
