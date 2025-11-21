using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;
using OneClick_WebApp.Middleware;
using OneClick_WebApp.Models.Enums;
using OneClick_WebApp.Services;
using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.ResponseCompression;
using System.IO.Compression;

var builder = WebApplication.CreateBuilder(args);

// Core services
builder.Services.AddRazorPages();
builder.Services.AddHttpContextAccessor();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddHttpClient();

// Memory cache for performance optimisation
builder.Services.AddMemoryCache();

// Response compression - significant performance improvement
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[]
    {
        "image/svg+xml",
        "application/javascript",
        "application/json",
        "text/css",
        "text/html",
        "text/plain"
    });
});

// Configure compression levels
builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Optimal;
});

builder.Services.Configure<GzipCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Optimal;
});

// Output caching for frequently accessed pages
builder.Services.AddOutputCache(options =>
{
    // Cache product listings for 2 minutes
    options.AddPolicy("ProductsCache", builder =>
        builder.Expire(TimeSpan.FromMinutes(2))
               .Tag("products"));

    // Cache static content for 1 hour
    options.AddPolicy("StaticCache", builder =>
        builder.Expire(TimeSpan.FromHours(1))
               .Tag("static"));

    // Cache categories for 5 minutes
    options.AddPolicy("CategoriesCache", builder =>
        builder.Expire(TimeSpan.FromMinutes(5))
               .Tag("categories"));
});

// Session configuration
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(8);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

builder.Services.AddHttpClient("Groq", client =>
{
    client.BaseAddress = new Uri("https://api.groq.com/");
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
});

// Register custom services
builder.Services.AddSingleton<FirebaseDbService>();
builder.Services.AddSingleton<FirebaseStorageService>();
builder.Services.AddSingleton<CacheManagerService>();
builder.Services.AddScoped<FirebaseAuthService>();
builder.Services.AddScoped<SessionAuthService>();
builder.Services.AddScoped<CartService>();
builder.Services.AddScoped<POSIntegrationService>();

// Register background service for product sync
builder.Services.AddHostedService<ProductSyncBackgroundService>();

// Firebase configuration
var firebaseProjectId = builder.Configuration["Firebase:ProjectId"];
var firebaseCredentialsPath = builder.Configuration["Firebase:CredentialsPath"];

if (string.IsNullOrEmpty(firebaseProjectId) || string.IsNullOrEmpty(firebaseCredentialsPath))
{
    throw new InvalidOperationException("Firebase configuration is missing in appsettings.json");
}

Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", firebaseCredentialsPath);

builder.Services.AddSingleton(provider =>
{
    return FirestoreDb.Create(firebaseProjectId);
});

FirebaseApp.Create(new AppOptions()
{
    Credential = GoogleCredential.FromFile(firebaseCredentialsPath),
});

// Authentication setup
builder.Services.AddAuthentication("Session")
    .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(
        "Session", options => { });

// Authorisation policies
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireClaim("role", Role.Admin.ToString()));

    options.AddPolicy("IsAdmin", policy =>
        policy.RequireClaim("isAdmin", "true"));

    options.AddPolicy("OwnerOnly", policy =>
        policy.RequireClaim("isOwner", "true"));

    options.AddPolicy("ManagementAccess", policy =>
        policy.RequireAssertion(context =>
            context.User.HasClaim("isAdmin", "true") ||
            context.User.HasClaim("isManager", "true")));

    options.AddPolicy("StaffAccess", policy =>
        policy.RequireAssertion(context =>
            context.User.HasClaim("isAdmin", "true") ||
            context.User.HasClaim("isManager", "true") ||
            context.User.HasClaim("isStaff", "true")));

    options.AddPolicy("CanManageOrders", policy =>
        policy.RequireAssertion(context =>
            context.User.HasClaim("canManageOrders", "true") ||
            context.User.HasClaim("isAdmin", "true")));

    options.AddPolicy("CanViewOrders", policy =>
        policy.RequireAssertion(context =>
            context.User.HasClaim("canViewOrders", "true") ||
            context.User.HasClaim("canManageOrders", "true") ||
            context.User.HasClaim("isAdmin", "true")));

    options.AddPolicy("CanManageSettings", policy =>
        policy.RequireClaim("canManageSettings", "true"));

    options.AddPolicy("CanManageUsers", policy =>
        policy.RequireClaim("canManageUsers", "true"));

    options.AddPolicy("CanViewReports", policy =>
        policy.RequireClaim("canViewReports", "true"));

    options.AddPolicy("ErpUserOnly", policy =>
        policy.RequireClaim("isErpUser", "true"));

    options.AddPolicy("CustomerAccess", policy =>
        policy.RequireAuthenticatedUser());

    options.AddPolicy("BranchAccess", policy =>
        policy.RequireAssertion(context =>
            context.User.HasClaim("isAdmin", "true") ||
            context.User.Claims.Any(c => c.Type == "BranchAccess")));
});

builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var supportedCultures = new[]
    {
        new CultureInfo("en-ZA")
    };

    options.DefaultRequestCulture = new RequestCulture("en-ZA");
    options.SupportedCultures = supportedCultures;
    options.SupportedUICultures = supportedCultures;
});

var app = builder.Build();

// Configure HTTP pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

// Enable response compression early in pipeline
app.UseResponseCompression();

// Static files with caching headers
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // Cache static files for 30 days
        const int durationInSeconds = 60 * 60 * 24 * 30;
        ctx.Context.Response.Headers.Append("Cache-Control",
            $"public,max-age={durationInSeconds}");
    }
});

app.UseRouting();
app.UseRequestLocalization();
app.UseSession();
app.UseMiddleware<SessionAuthMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

// Enable output caching
app.UseOutputCache();

app.MapControllers();
app.MapRazorPages();

app.Run();

// Session authentication handler
public class SessionAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public SessionAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder, ISystemClock clock)
        : base(options, logger, encoder, clock)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Context.User.Identity != null && Context.User.Identity.IsAuthenticated)
        {
            var ticket = new AuthenticationTicket(Context.User, "Session");
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        return Task.FromResult(AuthenticateResult.NoResult());
    }
}