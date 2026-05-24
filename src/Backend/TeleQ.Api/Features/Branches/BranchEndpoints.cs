using FastEndpoints;
using FastEndpoints.AspVersioning;
using Microsoft.EntityFrameworkCore;
using TeleQ.Api.Data;

namespace TeleQ.Api.Features.Branches;

public sealed record BranchResponse(
    Guid Id,
    string Name,
    string Address,
    string? PhoneNumber,
    bool IsActive,
    DateTimeOffset CreatedAt);

public sealed record CreateBranchRequest(string Name, string Address, string? PhoneNumber);

public sealed record UpdateBranchRequest(string Name, string Address, string? PhoneNumber);

/// <summary>Returns all active branches ordered by name.</summary>
public sealed class GetBranchesEndpoint(AppDbContext db, BranchMapper mapper)
    : EndpointWithoutRequest<List<BranchResponse>>
{
    public override void Configure()
    {
        Get("/branches");
        Version(1);
        AllowAnonymous();
        Description(x => x.WithTags("Branches"));
        Options(x => x.WithVersionSet("TeleQ").MapToApiVersion(1.0));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var branches = await db.Branches
            .Where(b => b.IsActive)
            .OrderBy(b => b.Name)
            .ToListAsync(ct);

        await Send.OkAsync(branches.Select(mapper.FromEntity).ToList(), ct);
    }
}

/// <summary>Returns a single branch by its identifier.</summary>
public sealed class GetBranchEndpoint(AppDbContext db)
    : EndpointWithoutRequest<BranchResponse, BranchMapper>
{
    public override void Configure()
    {
        Get("/branches/{id:guid}");
        Version(1);
        AllowAnonymous();
        Description(x => x.WithTags("Branches"));
        Options(x => x.WithVersionSet("TeleQ").MapToApiVersion(1.0));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var branch = await db.Branches.FindAsync([id], ct);

        if (branch is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(Map.FromEntity(branch), ct);
    }
}

/// <summary>Creates a new branch. Restricted to Admin users.</summary>
public sealed class CreateBranchEndpoint(AppDbContext db)
    : Endpoint<CreateBranchRequest, BranchResponse, BranchMapper>
{
    public override void Configure()
    {
        Post("/branches");
        Version(1);
        Policies("AdminOnly");
        Description(x => x.WithTags("Branches"));
        Options(x => x.WithVersionSet("TeleQ").MapToApiVersion(1.0));
    }

    public override async Task HandleAsync(CreateBranchRequest req, CancellationToken ct)
    {
        var branch = Map.ToEntity(req);
        db.Branches.Add(branch);
        await db.SaveChangesAsync(ct);

        await Send.CreatedAtAsync<GetBranchEndpoint>(
            new { id = branch.Id },
            Map.FromEntity(branch),
            cancellation: ct);
    }
}

/// <summary>Updates an existing branch's details. Restricted to Admin users.</summary>
public sealed class UpdateBranchEndpoint(AppDbContext db) : Endpoint<UpdateBranchRequest>
{
    public override void Configure()
    {
        Put("/branches/{id:guid}");
        Version(1);
        Policies("AdminOnly");
        Description(x => x.WithTags("Branches"));
        Options(x => x.WithVersionSet("TeleQ").MapToApiVersion(1.0));
    }

    public override async Task HandleAsync(UpdateBranchRequest req, CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var branch = await db.Branches.FindAsync([id], ct);

        if (branch is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        branch.Name = req.Name;
        branch.Address = req.Address;
        branch.PhoneNumber = req.PhoneNumber;

        await db.SaveChangesAsync(ct);
        await Send.NoContentAsync(ct);
    }
}

/// <summary>Soft-deletes (deactivates) a branch. Restricted to Admin users.</summary>
public sealed class DeactivateBranchEndpoint(AppDbContext db) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/branches/{id:guid}");
        Version(1);
        Policies("AdminOnly");
        Description(x => x.WithTags("Branches"));
        Options(x => x.WithVersionSet("TeleQ").MapToApiVersion(1.0));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var branch = await db.Branches.FindAsync([id], ct);

        if (branch is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        branch.IsActive = false;
        await db.SaveChangesAsync(ct);
        await Send.NoContentAsync(ct);
    }
}
