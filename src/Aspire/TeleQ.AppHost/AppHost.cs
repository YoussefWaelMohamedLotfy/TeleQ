IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

var adminPassword = builder.AddParameter("Password", secret: true);

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
    .WaitFor(teleqDb)
    .WaitFor(keycloak);

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
    .WaitFor(api);

await builder.Build().RunAsync();
