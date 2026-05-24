using FastEndpoints;
using FastEndpoints.AspVersioning;
using Microsoft.EntityFrameworkCore;
using TeleQ.Api.Data;
using TeleQ.Api.Data.Entities;

namespace TeleQ.Api.Features.Services;

// ── Shared DTOs ───────────────────────────────────────────────────────────

public sealed record ServiceResponse(
    Guid Id,
    Guid BranchId,
    string Name,
    string? Description,
    int EstimatedDurationMinutes,
    bool IsActive,
    DateTimeOffset CreatedAt);

public static class ServiceMapper
{
    public static ServiceResponse ToResponse(this Service s) =>
        new(s.Id, s.BranchId, s.Name, s.Description, s.EstimatedDurationMinutes, s.IsActive, s.CreatedAt);
}

// ── GET /branches/{branchId}/services ─────────────────────────────────────

public sealed class GetServicesEndpoint(AppDbContext db) : EndpointWithoutRequest<List<ServiceResponse>>
{
    public override void Configure()
    {
        Get("/branches/{branchId:guid}/services");
        Version(1);
        AllowAnonymous();
        Options(x => x.WithVersionSet("TeleQ").MapToApiVersion(1.0));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var branchId = Route<Guid>("branchId");

        var services = await db.Services
            .Where(s => s.BranchId == branchId && s.IsActive)
            .OrderBy(s => s.Name)
            .ToListAsync(ct);

        await Send.OkAsync(services.Select(s => s.ToResponse()).ToList(), ct);
    }
}

// ── GET /services/{id} ────────────────────────────────────────────────────

public sealed class GetServiceEndpoint(AppDbContext db) : EndpointWithoutRequest<ServiceResponse>
{
    public override void Configure()
    {
        Get("/services/{id:guid}");
        Version(1);
        AllowAnonymous();
        Options(x => x.WithVersionSet("TeleQ").MapToApiVersion(1.0));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var service = await db.Services.FindAsync([id], ct);

        if (service is null) { await Send.NotFoundAsync(ct); return; }

        await Send.OkAsync(service.ToResponse(), ct);
    }
}

// ── POST /branches/{branchId}/services (Admin only) ───────────────────────

public sealed record CreateServiceRequest(
    string Name,
    string? Description,
    int EstimatedDurationMinutes);

public sealed class CreateServiceEndpoint(AppDbContext db) : Endpoint<CreateServiceRequest, ServiceResponse>
{
    public override void Configure()
    {
        Post("/branches/{branchId:guid}/services");
        Version(1);
        Policies("AdminOnly");
        Options(x => x.WithVersionSet("TeleQ").MapToApiVersion(1.0));
    }

    public override async Task HandleAsync(CreateServiceRequest req, CancellationToken ct)
    {
        var branchId = Route<Guid>("branchId");

        if (!await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .AnyAsync(db.Branches, b => b.Id == branchId && b.IsActive, ct))
        {
            AddError("Branch not found or inactive.");
            await Send.ErrorsAsync(404, ct);
            return;
        }

        var service = new Service
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            Name = req.Name,
            Description = req.Description,
            EstimatedDurationMinutes = req.EstimatedDurationMinutes,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.Services.Add(service);
        await db.SaveChangesAsync(ct);

        await Send.CreatedAtAsync<GetServiceEndpoint>(
            new { id = service.Id },
            service.ToResponse(),
            cancellation: ct);
    }
}

// ── PUT /services/{id} (Admin only) ──────────────────────────────────────

public sealed record UpdateServiceRequest(
    string Name,
    string? Description,
    int EstimatedDurationMinutes);

public sealed class UpdateServiceEndpoint(AppDbContext db) : Endpoint<UpdateServiceRequest>
{
    public override void Configure()
    {
        Put("/services/{id:guid}");
        Version(1);
        Policies("AdminOnly");
        Options(x => x.WithVersionSet("TeleQ").MapToApiVersion(1.0));
    }

    public override async Task HandleAsync(UpdateServiceRequest req, CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var service = await db.Services.FindAsync([id], ct);

        if (service is null) { await Send.NotFoundAsync(ct); return; }

        service.Name = req.Name;
        service.Description = req.Description;
        service.EstimatedDurationMinutes = req.EstimatedDurationMinutes;

        await db.SaveChangesAsync(ct);
        await Send.NoContentAsync(ct);
    }
}

// ── DELETE /services/{id} (Admin only — soft delete) ─────────────────────

public sealed class DeactivateServiceEndpoint(AppDbContext db) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/services/{id:guid}");
        Version(1);
        Policies("AdminOnly");
        Options(x => x.WithVersionSet("TeleQ").MapToApiVersion(1.0));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var service = await db.Services.FindAsync([id], ct);

        if (service is null) { await Send.NotFoundAsync(ct); return; }

        service.IsActive = false;
        await db.SaveChangesAsync(ct);
        await Send.NoContentAsync(ct);
    }
}
