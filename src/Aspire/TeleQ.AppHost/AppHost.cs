IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

var adminPassword = builder.AddParameter("Password", secret: true);

var garnet = builder
    .AddGarnet("garnet", 6379, adminPassword)
    .WithImage("microsoft/garnet-alpine")
    .WithLifetime(ContainerLifetime.Persistent);

var postgres = builder
    .AddPostgres("postgres", password: adminPassword, port: 5432)
    .WithImageTag("alpine")
    //.WithDataVolume()
    .WithVolume("teleq-pg-data", "/var/lib/postgresql")    
    .WithLifetime(ContainerLifetime.Persistent);

postgres.WithPgAdmin(x => x.WithImageTag("latest").WithHostPort(5050).WithParentRelationship(postgres).WithLifetime(ContainerLifetime.Persistent));

var teleqDb = postgres.AddDatabase("TeleQ-Db");
var KeycloakDb = postgres.AddDatabase("Keycloak-Db");

var keycloak = builder
    .AddKeycloak("keycloak", 8081, adminPassword: adminPassword)
    .WithImageTag("latest")
    .WithPostgres(KeycloakDb)
    .WaitFor(KeycloakDb)
    .WithRealmImport("./Realms")
    .WithLifetime(ContainerLifetime.Persistent);

var rabbitmq = builder.AddRabbitMQ("rabbitmq", password: adminPassword, port: 5672)
    .WithImageTag("management-alpine")
    .WithManagementPlugin(15672)
    .WithLifetime(ContainerLifetime.Persistent);

var api = builder
    .AddProject<Projects.TeleQ_Api>("api")
    .WithReference(teleqDb)
    .WithReference(keycloak)
    .WithReference(garnet)
    .WithReference(rabbitmq)
    .WaitFor(teleqDb)
    .WaitFor(keycloak)
    .WaitFor(rabbitmq)
    .WaitFor(garnet);

var apiMigrations = api.AddEFMigrations("api-migrations", "TeleQ.Api.Data.AppDbContext")
    .WaitFor(teleqDb)
    .WithMigrationsProject<Projects.TeleQ_Api>()
    .RunDatabaseUpdateOnStart();

api.WaitForCompletion(apiMigrations)
    .WithChildRelationship(apiMigrations);

var blazor = builder
    .AddProject<Projects.TeleQ_Web>("blazor")
    .WithReference(api)
    .WithReference(keycloak)
    .WithReference(garnet)
    .WaitFor(api)
    .WaitFor(garnet);

var ngrokAuthToken = builder.AddParameter("ngrok-auth-token", true);



var messagingWorker = builder.AddProject<Projects.TeleQ_Messaging_Worker>("teleq-messaging-worker")
    .WithReference(teleqDb)
    .WithReference(garnet)
    .WithReference(rabbitmq)
    .WaitFor(teleqDb)
    .WaitFor(garnet)
    .WaitFor(rabbitmq)
    .WithEnvironment("TelegramBot__FrontendBaseUrl", blazor.GetEndpoint("https"));

var ngrok = builder.AddNgrok("ngrok", endpointPort: 4040)
    .WithImageTag("alpine")
    .WithAuthToken(ngrokAuthToken)
    .WithTunnelEndpoint(messagingWorker, "https")
    .WithLifetime(ContainerLifetime.Persistent);

messagingWorker.WithEnvironment("TelegramBot__NgrokManagementUrl", ngrok.GetEndpoint("http"));

await builder.Build().RunAsync();
