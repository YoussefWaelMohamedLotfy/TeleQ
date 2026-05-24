using Microsoft.AspNetCore.SignalR.Client;

namespace TeleQ.Web.Services;

public sealed class HubConnectionFactory(IConfiguration configuration)
{
    public HubConnection CreateQueueHubConnection()
    {
        var baseUrl = configuration["services__api__http__0"]
            ?? configuration["services:api:http:0"]
            ?? configuration["services__api__https__0"]
            ?? configuration["services:api:https:0"]
            ?? "http://api";

        return new HubConnectionBuilder()
            .WithUrl($"{baseUrl.TrimEnd('/')}/hubs/queue")
            .WithAutomaticReconnect()
            .Build();
    }
}
