using MassTransit;
using Marten;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Serialization;
using Telegram.Bot.Types;
using TeleQ.Messaging.Shared.Aggregates;
using TeleQ.Messaging.Shared.Configuration;
using TeleQ.Messaging.Shared.DomainEvents;
using TeleQ.Messaging.Worker.Consumers;
using TeleQ.Messaging.Worker.Data;
using TeleQ.Messaging.Worker.Telegram;
using ZiggyCreatures.Caching.Fusion;
using JasperFx;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddNpgsqlDbContext<WorkerDbContext>("TeleQ-Db");

builder.Services.AddMarten(opts =>
    {
        opts.Connection(builder.Configuration.GetConnectionString("TeleQ-Db")!);
        opts.DatabaseSchemaName = "events";
        opts.UseSystemTextJsonForSerialization();
        opts.AutoCreateSchemaObjects = AutoCreate.None;

        opts.Schema.For<Ticket>().Identity(x => x.Id);

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
    .UseLightweightSessions();

builder.AddRedisDistributedCache("garnet");
builder.Services.AddFusionCache()
    .WithSystemTextJsonSerializer()
    .WithRegisteredDistributedCache()
    .WithStackExchangeRedisBackplane(x => x.Configuration = builder.Configuration.GetConnectionString("garnet"))
    .AsHybridCache();

builder.Services.Configure<TelegramBotOptions>(
    builder.Configuration.GetSection(TelegramBotOptions.SectionName));

builder.Services.AddHttpClient();

builder.Services.AddSingleton<ITelegramBotClient>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<TelegramBotOptions>>().Value;
    return new TelegramBotClient(opts.BotToken);
});

builder.Services.AddSingleton<TelegramUpdateHandler>();
builder.Services.AddHostedService<TelegramBotService>();

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<SendTelegramMessageConsumer>();
    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host(builder.Configuration.GetConnectionString("rabbitmq"));
        cfg.ConfigureEndpoints(ctx);
    });
});

var app = builder.Build();

app.MapDefaultEndpoints();

// Telegram webhook endpoint — Telegram POSTs updates here when webhook mode is active.
// In long-polling mode this endpoint is never called but is harmless to expose.
app.MapPost("/bot/telegram", async (
    HttpContext ctx,
    TelegramUpdateHandler handler,
    ITelegramBotClient botClient,
    IOptions<TelegramBotOptions> opts) =>
{
    var secret = opts.Value.WebhookSecretToken;
    if (!string.IsNullOrWhiteSpace(secret))
    {
        var incoming = ctx.Request.Headers["X-Telegram-Bot-Api-Secret-Token"].FirstOrDefault();
        if (!string.Equals(incoming, secret, StringComparison.Ordinal))
            return Results.Unauthorized();
    }

    // Use Telegram.Bot's own JSON deserializer so all Telegram types round-trip correctly.
    var update = await ctx.Request.ReadFromJsonAsync<Update>(JsonBotAPI.Options, ctx.RequestAborted);
    if (update is null)
        return Results.BadRequest();

    _ = Task.Run(() => handler.HandleUpdateAsync(botClient, update, CancellationToken.None));
    return Results.Ok();
}).AllowAnonymous();

await app.RunAsync();
