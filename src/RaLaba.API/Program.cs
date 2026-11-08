using Microsoft.EntityFrameworkCore;
using RaLaba.API.Middleware;
using RaLaba.Application.Interfaces;
using RaLaba.Application.Services;
using RaLaba.Domain.Interfaces;
using RaLaba.Infrastructure.Persistence;
using RaLaba.Infrastructure.Repositories;
using Serilog;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using RaLaba.API.Security;
using Npgsql;
using RaLaba.API.Integrations;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();
builder.Host.UseSerilog();

// Database
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("Configure ConnectionStrings:DefaultConnection through user secrets or the server environment.");

builder.Services.AddDbContext<RaLabaDbContext>(options =>
    options.UseNpgsql(connectionString));

var loyverse = builder.Configuration.GetSection("Loyverse").Get<LoyverseOptions>() ?? new();
builder.Services.AddSingleton(loyverse);
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
builder.Services.AddHttpClient<LoyverseClient>(client => client.Timeout = TimeSpan.FromSeconds(45))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddScoped<LoyverseSync>();
builder.Services.AddSingleton<LoyverseWorker>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<LoyverseWorker>());

// Dependency Injection
builder.Services.AddScoped<IReceiptRepository, ReceiptRepository>();
builder.Services.AddScoped<IReceiptService, ReceiptService>();

builder.Services.AddOptions<SupabaseAuthOptions>()
    .Bind(builder.Configuration.GetSection("SupabaseAuth"))
    .Validate(o => Uri.TryCreate(o.Url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && uri.AbsolutePath == "/"
        && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query)
        && string.IsNullOrEmpty(uri.Fragment), "SupabaseAuth:Url must be an HTTPS project origin.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.PublishableKey), "Configure a Supabase publishable key.")
    .ValidateOnStart();
builder.Services.AddHttpClient(SupabaseAuthenticationHandler.HttpClientName, client =>
    client.Timeout = TimeSpan.FromSeconds(10))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddAuthentication(SupabaseAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, SupabaseAuthenticationHandler>(
        SupabaseAuthenticationHandler.SchemeName, _ => { });
var staffIds = builder.Configuration.GetSection("SupabaseAuth:StaffUserIds").Get<Guid[]>() ?? Array.Empty<Guid>();
builder.Services.AddAuthorization(options =>
{
    var staffPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireAssertion(context => Guid.TryParse(
            context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
            out var id) && staffIds.Contains(id)).Build();
    options.DefaultPolicy = staffPolicy;
    options.FallbackPolicy = staffPolicy;
});

// CORS - frontend dev server
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
        policy.WithOrigins(builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? new[] { "http://localhost:5173" })
              .AllowAnyHeader()
              .AllowAnyMethod());
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Render terminates public TLS at its edge; its internal health probes use HTTP.
if (!string.Equals(Environment.GetEnvironmentVariable("RENDER"), "true", StringComparison.OrdinalIgnoreCase))
    app.UseHttpsRedirection();
app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

app.Run();

public partial class Program { }
