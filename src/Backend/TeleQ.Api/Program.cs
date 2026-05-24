using FastEndpoints;
using FastEndpoints.Swagger;
using JasperFx;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Scalar.AspNetCore;
using TeleQ.Api.Common.Aggregates;
using TeleQ.Api.Common.DomainEvents;
using TeleQ.Api.Common.Projections;
using TeleQ.Api.Data;
using TeleQ.Api.Features.Notifications;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.WebHost.ConfigureKestrel(x => x.AddServerHeader = false);

builder.Services.AddProblemDetails();

builder.Services.AddFastEndpoints()
    .SwaggerDocument(o =>
    {
        o.MaxEndpointVersion = 1;
        o.DocumentSettings = s =>
        {
            s.Title = "TeleQ API";
            s.Version = "v1";
            s.DocumentName = "v1";
        };
    });

builder.AddNpgsqlDbContext<AppDbContext>("TeleQ-Db");

builder.Services.AddMarten(opts =>
    {
        opts.Connection(builder.Configuration.GetConnectionString("TeleQ-Db")!);
        opts.DatabaseSchemaName = "events";

        opts.Schema.For<Ticket>().Identity(x => x.Id);

        // Inline projections update synchronously on event append (low latency reads)
        opts.Projections.Add<BranchQueueProjection>(ProjectionLifecycle.Inline);

        // Async projection runs in background via Marten daemon (non-critical stats)
        opts.Projections.Add<DailyQueueStatsProjection>(ProjectionLifecycle.Async);

        if (builder.Environment.IsDevelopment())
        {
            opts.AutoCreateSchemaObjects = AutoCreate.All;
        }

        // Register all domain event types
        opts.Events.AddEventTypes(
        [
            typeof(TicketIssued),
            typeof(AppointmentBooked),
            typeof(TicketCalled),
            typeof(TicketServed),
            typeof(TicketNoShow),
            typeof(TicketCancelled),
            typeof(TicketRescheduled)
        ]);
    })
    .UseLightweightSessions()
    .AddAsyncDaemon(DaemonMode.HotCold);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddKeycloakJwtBearer(
        serviceName: "keycloak",
        realm: "teleq",
        options =>
        {
            options.Audience = "teleq-api";
            options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("AdminOnly", p => p.RequireRole("admin"))
    .AddPolicy("ClerkOrAdmin", p => p.RequireRole("clerk", "admin"))
    .AddPolicy("AnyStaff", p => p.RequireRole("clerk", "admin"));

builder.Services.AddSignalR();

builder.Services.AddMediator();

builder.Services.Configure<TelegramBotOptions>(
    builder.Configuration.GetSection(TelegramBotOptions.SectionName));
builder.Services.AddHostedService<TelegramBotService>();


WebApplication app = builder.Build();

app.MapDefaultEndpoints();

// ── EF Core schema bootstrap ──────────────────────────────────────────────
// EnsureCreated is used instead of Migrations due to a design-time tooling
// conflict between Marten's CodeAnalysis 4.x pin and EF tools requiring 5.x.
// In development Aspire spins up a fresh Postgres container so this is safe.
//await using (AsyncServiceScope scope = app.Services.CreateAsyncScope())
//{
//    AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
//    await db.Database.EnsureCreatedAsync();
//}

app.UseExceptionHandler(_ => { });
app.UseStatusCodePages();

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.UseFastEndpoints(c =>
{
    c.Versioning.Prefix = "v";
    c.Versioning.PrependToRoute = true;
});

app.MapHub<QueueHub>("/hubs/queue");

app.UseOpenApi(c => c.Path = "/openapi/{documentName}.json");
app.MapScalarApiReference(o => o.AddDocument("v1"));

await app.RunAsync();
