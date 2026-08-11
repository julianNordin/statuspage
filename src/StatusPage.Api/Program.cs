using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using StatusPage.Infrastructure.Identity;
using Scalar.AspNetCore;
using StatusPage.Api.Infrastructure;
using StatusPage.Infrastructure;
using StatusPage.Infrastructure.Queries;

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

// Injected rather than reached for, so every test can decide what "now" is and no rule in
// this codebase is written against the wall clock.
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddControllers(options =>
    options.Filters.Add<ProblemDetailsContentTypeFilter>());


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

// Migrate and seed before serving. The operators listed in configuration are the only way
// an account comes into being; there is no registration endpoint anywhere in this API.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<StatusPageDbContext>();
    await db.Database.MigrateAsync().ConfigureAwait(false);

    var seeds = app.Configuration.GetSection("Operators").Get<SeededOperator[]>() ?? [];
    if (seeds.Length > 0)
    {
        await scope.ServiceProvider
            .GetRequiredService<OperatorSeeder>()
            .SeedAsync(seeds)
            .ConfigureAwait(false);
    }
}

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
