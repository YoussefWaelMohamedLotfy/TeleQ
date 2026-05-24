using FastEndpoints;
using FastEndpoints.AspVersioning;
using Microsoft.EntityFrameworkCore;
using TeleQ.Api.Data;
using TeleQ.Api.Data.Entities;

namespace TeleQ.Api.Features.Branches;

// ── Shared DTOs ───────────────────────────────────────────────────────────

public sealed record BranchResponse(
    Guid Id,
    string Name,
    string Address,
    string? PhoneNumber,
    bool IsActive,
    DateTimeOffset CreatedAt);

public static class BranchMapper
{
    public static BranchResponse ToResponse(this Branch b) =>
        new(b.Id, b.Name, b.Address, b.PhoneNumber, b.IsActive, b.CreatedAt);
}

// ── GET /branches ─────────────────────────────────────────────────────────

public sealed class GetBranchesEndpoint(AppDbContext db) : EndpointWithoutRequest<List<BranchResponse>>
{
    public override void Configure()
    {
        Get("/branches");
        Version(1);
        AllowAnonymous();
        Options(x => x.WithVersionSet("TeleQ").MapToApiVersion(1.0));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var branches = await db.Branches
            .Where(b => b.IsActive)
            .OrderBy(b => b.Name)
            .ToListAsync(ct);

        await Send.OkAsync(branches.Select(b => b.ToResponse()).ToList(), ct);
    }
}

// ── GET /branches/{id} ────────────────────────────────────────────────────

public sealed class GetBranchEndpoint(AppDbContext db) : EndpointWithoutRequest<BranchResponse>
{
    public override void Configure()
    {
        Get("/branches/{id:guid}");
        Version(1);
        AllowAnonymous();
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

        await Send.OkAsync(branch.ToResponse(), ct);
    }
}

// ── POST /branches (Admin only) ───────────────────────────────────────────

public sealed record CreateBranchRequest(string Name, string Address, string? PhoneNumber);

public sealed class CreateBranchEndpoint(AppDbContext db) : Endpoint<CreateBranchRequest, BranchResponse>
{
    public override void Configure()
    {
        Post("/branches");
        Version(1);
        Policies("AdminOnly");
        Options(x => x.WithVersionSet("TeleQ").MapToApiVersion(1.0));
    }

    public override async Task HandleAsync(CreateBranchRequest req, CancellationToken ct)
    {
        var branch = new Branch
        {
            Id = Guid.NewGuid(),
            Name = req.Name,
            Address = req.Address,
            PhoneNumber = req.PhoneNumber,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.Branches.Add(branch);
        await db.SaveChangesAsync(ct);

        await Send.CreatedAtAsync<GetBranchEndpoint>(
            new { id = branch.Id },
            branch.ToResponse(),
            cancellation: ct);
    }
}

// ── PUT /branches/{id} (Admin only) ──────────────────────────────────────

public sealed record UpdateBranchRequest(string Name, string Address, string? PhoneNumber);

public sealed class UpdateBranchEndpoint(AppDbContext db) : Endpoint<UpdateBranchRequest>
{
    public override void Configure()
    {
        Put("/branches/{id:guid}");
        Version(1);
        Policies("AdminOnly");
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

// ── DELETE /branches/{id} (Admin only — soft delete) ─────────────────────

public sealed class DeactivateBranchEndpoint(AppDbContext db) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/branches/{id:guid}");
        Version(1);
        Policies("AdminOnly");
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
