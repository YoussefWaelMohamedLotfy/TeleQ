using FastEndpoints;
using FastEndpoints.AspVersioning;
using Microsoft.EntityFrameworkCore;
using TeleQ.Api.Data;

namespace TeleQ.Api.Features.Services;

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

        if (!await EntityFrameworkQueryableExtensions
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
