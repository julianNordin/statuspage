using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using StatusPage.Infrastructure.Identity;
using Scalar.AspNetCore;
using StatusPage.Api.Infrastructure;
using StatusPage.Infrastructure;
using StatusPage.Infrastructure.Queries;
using StatusPage.Infrastructure.ReadModel;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<StatusPageDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("Default"),
        sql => sql.EnableRetryOnFailure()));

builder.Services
    .AddIdentityCore<OperatorAccount>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 12;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<StatusPageDbContext>()
    .AddSignInManager();

builder.Services
    .AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.Section))
    .ValidateDataAnnotations()
    // Validated at startup rather than on first use: a deployment with no signing key should
    // fail to start, not issue tokens nobody can verify.
    .ValidateOnStart();

builder.Services.AddSingleton<TokenIssuer>();
builder.Services.AddScoped<OperatorSeeder>();

var jwt = builder.Configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            // No grace period on expiry. The default five minutes is a convenience for clocks
            // that drift, and every server here gets its time from the platform.
            ClockSkew = TimeSpan.Zero,
        };
    });

builder.Services.AddAuthorization(options =>
{
    // Protected by default. Forgetting [Authorize] on a new controller fails closed rather
    // than open, so the mistake is a 401 in a test instead of an open write endpoint in
    // production. Everything public says [AllowAnonymous] out loud.
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddScoped<ComponentQueries>();
builder.Services.AddScoped<StatusQueries>();
builder.Services.AddScoped<IncidentQueries>();
builder.Services.AddReadModel(builder.Configuration);

// Injected rather than reached for, so every test can decide what "now" is and no rule in
// this codebase is written against the wall clock.
builder.Services.AddSingleton(TimeProvider.System);

builder.Services
    .AddControllers(options => options.Filters.Add<ProblemDetailsContentTypeFilter>())
    .AddJsonOptions(options =>
        // Enums as names. The console switches on them and a reader debugging a response
        // should not have to count enum members to find out what 2 meant.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));


// RFC 9457 for everything: validation failures, 404s from the framework, and whatever
// escapes a controller all come back as application/problem+json.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();

builder.Services.AddOpenApi();

// The SPA is served from Static Web Apps, on a different origin to the API. Named rather
// than default, and read from configuration rather than hardcoded, because the origin is a
// property of the deployment and not of the code.
const string SpaCors = "spa";
builder.Services.AddCors(options => options.AddPolicy(SpaCors, policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

// Seed before serving. The operators listed in configuration are the only way an account
// comes into being; there is no registration endpoint anywhere in this API.
//
// Migrating here is opt-in and off by default. With more than one replica it is a race —
// two instances reach for the same lock and the loser waits or fails depending on the
// provider's mood — and a failed migration crash-loops a container instead of stopping a
// deployment. The migration bundle image runs as a one-shot job that must finish first.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<StatusPageDbContext>();

    if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
    {
        await db.Database.MigrateAsync().ConfigureAwait(false);
    }

    // The page reads the snapshot straight from blob storage, from a different origin, so
    // the storage account has to say a browser may. In Azure the template declares this and
    // the deployed identity cannot change it; locally there is no template.
    if (app.Configuration.GetValue("ReadModel:ConfigureCors", false))
    {
        await scope.ServiceProvider
            .GetRequiredService<ReadModelCors>()
            .ConfigureAsync(app.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
            .ConfigureAwait(false);
    }

    var seeds = app.Configuration.GetSection("Operators").Get<SeededOperator[]>() ?? [];
    if (seeds.Length > 0)
    {
        await scope.ServiceProvider
            .GetRequiredService<OperatorSeeder>()
            .SeedAsync(seeds)
            .ConfigureAwait(false);
    }
}

// TLS terminates at the ingress, so the container itself is spoken to over plain HTTP on
// 8080 and every request arrives claiming that scheme. Absolute URLs the API generates
// inherit it: the Location header on a 201 came back as http://, which a strict client
// refuses to follow and a lax one follows in the clear. The forwarded headers carry what the
// caller actually used, and this is what makes the app read them.
//
// Nothing local can catch this. In Compose there is no proxy in front of the container, so
// the scheme really is http and the header really is absent — the bug exists only where the
// deployment does, which is why it survived to the first real environment.
//
// The known-network and known-proxy defaults trust loopback only, and the ingress is not
// loopback, so they are cleared. That is safe precisely here: the container is reachable
// only through that ingress, so there is no route by which an untrusted caller could set
// these headers itself.
var forwardedHeaders = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor,
};
// KnownIPNetworks, not KnownNetworks: the latter is obsolete in .NET 10 and, with warnings
// as errors, using it is a build failure rather than a hint.
forwardedHeaders.KnownIPNetworks.Clear();
forwardedHeaders.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeaders);

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseCors(SpaCors);
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

await app.RunAsync().ConfigureAwait(false);

/// <summary>
/// Named so the integration tests can drive the real application through
/// <c>WebApplicationFactory</c> rather than a rebuilt approximation of it.
/// </summary>
public partial class Program;
