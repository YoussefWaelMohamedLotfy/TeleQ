using Asp.Versioning;
using FastEndpoints;
using FastEndpoints.AspVersioning;
using FastEndpoints.OpenApi;
using JasperFx;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Scalar.AspNetCore;
using TeleQ.Api.Common.Aggregates;
using TeleQ.Api.Common.DomainEvents;
using TeleQ.Api.Common.Projections;
using TeleQ.Api.Data;
using TeleQ.Api.Features.Branches;
using TeleQ.Api.Features.Notifications;
using TeleQ.Api.Features.Services;
using TeleQ.Api.Features.Tickets;
using TeleQ.Api.Features.TimeSlots;
using TeleQ.Api.OpenAPI;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Define the API version set used across all endpoints.
// New versions are registered here and mapped to endpoints via Options().
VersionSets.CreateApi("TeleQ", v => v
    .HasApiVersion(new ApiVersion(1, 0))
    .HasApiVersion(new ApiVersion(2, 0)));

builder.AddServiceDefaults();

builder.WebHost.ConfigureKestrel(x => x.AddServerHeader = false);

builder.Services.AddProblemDetails();

// Mappers constructor-injected into list endpoints must be registered explicitly,
// as FastEndpoints only auto-registers mappers used as generic type parameters.
builder.Services.AddSingleton<BranchMapper>();
builder.Services.AddSingleton<ServiceMapper>();
builder.Services.AddSingleton<TimeSlotMapper>();
builder.Services.AddSingleton<TicketMapper>();

builder.AddRedisDistributedCache("garnet");
builder.Services.AddHybridCache();

builder.Services.AddFastEndpoints()
    .AddVersioning(o =>
    {
        o.DefaultApiVersion = new ApiVersion(1, 0);
        o.AssumeDefaultVersionWhenUnspecified = true;
        o.ReportApiVersions = true;
        // Header versioning: X-Api-Version: 1
        // URL prefix (/v1/...) is handled by FastEndpoints' built-in PrependToRoute.
        o.ApiVersionReader = new HeaderApiVersionReader("X-Api-Version");
    })
    .AddOpenApi(options =>
    {
        options.AddScalarTransformers();
        options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
        options.AddDocumentTransformer<AuthorizedOperationTransformer>();

        options.AddDocumentTransformer(
            (document, context, cancellationToken) =>
            {
                document.Info.Contact = new()
                {
                    Name = "TeleQ Support",
                    Email = "info@teleq.com",
                };
                return Task.CompletedTask;
            }
        );
    });

builder.AddNpgsqlDbContext<AppDbContext>("TeleQ-Db", configureDbContextOptions: opts =>
{
    opts.UseSeeding(AppDbSeeder.Seed)
        .UseAsyncSeeding(AppDbSeeder.SeedAsync);
});

builder.Services.AddMarten(opts =>
    {
        opts.Connection(builder.Configuration.GetConnectionString("TeleQ-Db")!);
        opts.DatabaseSchemaName = "events";

        opts.UseSystemTextJsonForSerialization();

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
    .AddFallbackPolicy("Default", p => p.RequireAuthenticatedUser())
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

app.UseExceptionHandler(_ => { });
app.UseStatusCodePages();

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.UseFastEndpoints(c =>
{
    c.Versioning.Prefix = "v";
    c.Versioning.DefaultVersion = 1;
    c.Versioning.PrependToRoute = true;
});

app.MapHub<QueueHub>("/hubs/queue");

app.MapOpenApi().AllowAnonymous();
app.MapScalarApiReference(options =>
{
    options
        .WithTheme(ScalarTheme.Default)
        .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient)
        .AddAuthorizationCodeFlow(
            OpenIdConnectDefaults.AuthenticationScheme,
            x =>
            {
                x.AuthorizationUrl =
                    "https://localhost:8081/realms/teleq/protocol/openid-connect/auth";
                x.TokenUrl = "https://localhost:8081/realms/teleq/protocol/openid-connect/token";
                x.Pkce = Pkce.Sha256;
                x.RedirectUri = "https://localhost:7157/scalar/v1";
                x.ClientId = "teleq-api";
                x.ClientSecret = "test";
                x.SelectedScopes = ["openid", "profile", "email", "offline_access"];
            }
        )
        .AddPreferredSecuritySchemes(OpenIdConnectDefaults.AuthenticationScheme);

    var descriptions = app.DescribeApiVersions();

    for (var i = 0; i < descriptions.Count; i++)
    {
        var description = descriptions[i];
        var isDefault = i == descriptions.Count - 1;
        options.AddDocument(description.GroupName, description.GroupName, isDefault: isDefault);
    }
}).AllowAnonymous();

await app.RunAsync();
