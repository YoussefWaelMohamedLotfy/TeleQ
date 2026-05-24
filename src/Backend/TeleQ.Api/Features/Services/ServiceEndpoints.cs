using FastEndpoints;
using FastEndpoints.AspVersioning;
using Microsoft.EntityFrameworkCore;
using TeleQ.Api.Data;

namespace TeleQ.Api.Features.Services;

public sealed record ServiceResponse(
    Guid Id,
    Guid BranchId,
    string Name,
    string? Description,
    int EstimatedDurationMinutes,
    bool IsActive,
    DateTimeOffset CreatedAt);

public sealed record CreateServiceRequest(
    string Name,
    string? Description,
    int EstimatedDurationMinutes);

public sealed record UpdateServiceRequest(
    string Name,
    string? Description,
    int EstimatedDurationMinutes);

/// <summary>Returns all active services for a branch, ordered by name.</summary>
public sealed class GetServicesEndpoint(AppDbContext db, ServiceMapper mapper)
    : EndpointWithoutRequest<List<ServiceResponse>>
{
    public override void Configure()
    {
        Get("/branches/{branchId:guid}/services");
        Version(1);
        AllowAnonymous();
        Description(x => x.WithTags("Services"));
        Options(x => x.WithVersionSet("TeleQ").MapToApiVersion(1.0));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var branchId = Route<Guid>("branchId");

        var services = await db.Services
            .Where(s => s.BranchId == branchId && s.IsActive)
            .OrderBy(s => s.Name)
            .ToListAsync(ct);

        await Send.OkAsync(services.Select(mapper.FromEntity).ToList(), ct);
    }
}

/// <summary>Returns a single service by its identifier.</summary>
public sealed class GetServiceEndpoint(AppDbContext db)
    : EndpointWithoutRequest<ServiceResponse, ServiceMapper>
{
    public override void Configure()
    {
        Get("/services/{id:guid}");
        Version(1);
        AllowAnonymous();
        Description(x => x.WithTags("Services"));
        Options(x => x.WithVersionSet("TeleQ").MapToApiVersion(1.0));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var service = await db.Services.FindAsync([id], ct);

        if (service is null) { await Send.NotFoundAsync(ct); return; }

        await Send.OkAsync(Map.FromEntity(service), ct);
    }
}

/// <summary>Creates a new service under a branch. Restricted to Admin users.</summary>
public sealed class CreateServiceEndpoint(AppDbContext db)
    : Endpoint<CreateServiceRequest, ServiceResponse, ServiceMapper>
{
    public override void Configure()
    {
        Post("/branches/{branchId:guid}/services");
        Version(1);
        Policies("AdminOnly");
        Description(x => x.WithTags("Services"));
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

        var service = Map.ToEntity(req);
        service.BranchId = branchId;

        db.Services.Add(service);
        await db.SaveChangesAsync(ct);

        await Send.CreatedAtAsync<GetServiceEndpoint>(
            new { id = service.Id },
            Map.FromEntity(service),
            cancellation: ct);
    }
}

/// <summary>Updates an existing service's details. Restricted to Admin users.</summary>
public sealed class UpdateServiceEndpoint(AppDbContext db) : Endpoint<UpdateServiceRequest>
{
    public override void Configure()
    {
        Put("/services/{id:guid}");
        Version(1);
        Policies("AdminOnly");
        Description(x => x.WithTags("Services"));
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

/// <summary>Soft-deletes (deactivates) a service. Restricted to Admin users.</summary>
public sealed class DeactivateServiceEndpoint(AppDbContext db) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/services/{id:guid}");
        Version(1);
        Policies("AdminOnly");
        Description(x => x.WithTags("Services"));
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
