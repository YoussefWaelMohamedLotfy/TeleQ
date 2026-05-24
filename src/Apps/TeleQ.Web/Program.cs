using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using TeleQ.Web.Components;
using TeleQ.Web.Services;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ApiAccessTokenHandler>();
builder.Services.AddScoped<HubConnectionFactory>();

// Garnet (Redis-compatible) as L2 distributed cache — registered before HybridCache
// so HybridCache automatically picks it up as its backing IDistributedCache.
builder.AddRedisDistributedCache("garnet");

// HybridCache: L1 in-memory + L2 Garnet. Keeps the auth ticket cookie tiny (GUID only).
builder.Services.AddHybridCache();
builder.Services.AddSingleton<ServerSideTicketStore>();

// Wire the ticket store via Configure<T> so the singleton is properly injected
// without triggering an early BuildServiceProvider() call.
builder.Services
    .AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
    .Configure<ServerSideTicketStore>((opts, store) =>
    {
        opts.Cookie.Name = "teleq.session";
        opts.Cookie.HttpOnly = true;
        opts.Cookie.SameSite = SameSiteMode.Lax;
        opts.SessionStore = store;
    });

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddKeycloakOpenIdConnect("keycloak", realm: "teleq", options =>
    {
        options.ClientId = "teleq-web";
        options.ClientSecret = "teleq-web-secret";
        options.ResponseType = "code";
        options.SaveTokens = true;
        options.GetClaimsFromUserInfoEndpoint = true;

        if (builder.Environment.IsDevelopment())
        {
            options.RequireHttpsMetadata = false;
        }
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("AdminOnly", p => p.RequireRole("admin"))
    .AddPolicy("ClerkOrAdmin", p => p.RequireRole("clerk", "admin"));

builder.Services.AddHttpClient<TeleQApiClient>(client =>
    {
        client.BaseAddress = new Uri("https+http://api");
    })
    .AddHttpMessageHandler<ApiAccessTokenHandler>();

WebApplication app = builder.Build();

app.MapDefaultEndpoints();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/login", async (HttpContext httpContext, string? returnUrl) =>
{
    var redirectUri = NormalizeReturnUrl(returnUrl);
    await httpContext.ChallengeAsync(
        OpenIdConnectDefaults.AuthenticationScheme,
        new AuthenticationProperties { RedirectUri = redirectUri });
});

app.MapGet("/logout", async (HttpContext httpContext, string? returnUrl) =>
{
    var redirectUri = NormalizeReturnUrl(returnUrl);

    await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    await httpContext.SignOutAsync(
        OpenIdConnectDefaults.AuthenticationScheme,
        new AuthenticationProperties { RedirectUri = redirectUri });
});

app.MapGet("/Account/Login", (string? returnUrl) =>
    Results.Redirect($"/login?returnUrl={Uri.EscapeDataString(NormalizeReturnUrl(returnUrl))}"));

app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

await app.RunAsync();
return;

static string NormalizeReturnUrl(string? returnUrl)
{
    if (string.IsNullOrWhiteSpace(returnUrl))
    {
        return "/";
    }

    return Uri.TryCreate(returnUrl, UriKind.Relative, out _) ? returnUrl : "/";
}
