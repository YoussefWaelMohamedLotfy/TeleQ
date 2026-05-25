IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

var adminPassword = builder.AddParameter("Password", secret: true);

var garnet = builder
    .AddGarnet("garnet", 6379, adminPassword)
    .WithImage("microsoft/garnet-alpine")
    .WithLifetime(ContainerLifetime.Persistent);

var postgres = builder
    .AddPostgres("postgres", password: adminPassword, port: 5432)
    .WithImageTag("alpine")
    .WithDataVolume()
    .WithPgAdmin(x => x.WithImageTag("latest").WithHostPort(5050).WithLifetime(ContainerLifetime.Persistent))
    .WithLifetime(ContainerLifetime.Persistent);

var teleqDb = postgres.AddDatabase("TeleQ-Db");
var KeycloakDb = postgres.AddDatabase("Keycloak-Db");

var keycloak = builder
    .AddKeycloak("keycloak", 8081, adminPassword: adminPassword)
    .WithImageTag("latest")
    .WithPostgres(KeycloakDb)
    .WaitFor(KeycloakDb)
    .WithRealmImport("./Realms")
    .WithLifetime(ContainerLifetime.Persistent);

var api = builder
    .AddProject<Projects.TeleQ_Api>("api")
    .WithReference(teleqDb)
    .WithReference(keycloak)
    .WithReference(garnet)
    .WaitFor(teleqDb)
    .WaitFor(keycloak)
    .WaitFor(garnet);

// Ngrok tunnels the API's HTTPS endpoint so Telegram can reach it during local development.
// The API queries the ngrok management API on startup to discover its dynamically-assigned
// public URL, then registers that URL as the Telegram webhook automatically.
// Set your auth token in user secrets:
//   dotnet user-secrets set "ngrok-auth-token" "<your-ngrok-auth-token>"
var ngrokAuthToken = builder.AddParameter("ngrok-auth-token", secret: true);

var ngrok = builder.AddNgrok("ngrok", endpointPort: 4040)
    .WithImageTag("alpine")
    .WithAuthToken(ngrokAuthToken)
    .WithTunnelEndpoint(api, "https")
    .WithLifetime(ContainerLifetime.Persistent);

// Inject the ngrok management API URL dynamically using Aspire's endpoint reference
// so the correct host-mapped port is used regardless of what Aspire assigns.
api.WaitFor(ngrok)
   .WithEnvironment("TelegramBot__NgrokManagementUrl", ngrok.GetEndpoint("http"));

var apiMigrations = api.AddEFMigrations("api-migrations", "TeleQ.Api.Data.AppDbContext")
    .WaitFor(teleqDb)
    .WithMigrationsProject<Projects.TeleQ_Api>()
    .RunDatabaseUpdateOnStart();

api.WaitForCompletion(apiMigrations)
    .WithChildRelationship(apiMigrations);

_ = builder
    .AddProject<Projects.TeleQ_Web>("blazor")
    .WithReference(api)
    .WithReference(keycloak)
    .WithReference(garnet)
    .WaitFor(api)
    .WaitFor(garnet);

await builder.Build().RunAsync();
