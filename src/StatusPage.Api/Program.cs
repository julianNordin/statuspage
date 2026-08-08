using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using StatusPage.Api.Infrastructure;
using StatusPage.Infrastructure;
using StatusPage.Infrastructure.Queries;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<StatusPageDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("Default"),
        sql => sql.EnableRetryOnFailure()));

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

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseCors(SpaCors);
app.MapControllers();

await app.RunAsync().ConfigureAwait(false);

/// <summary>
/// Named so the integration tests can drive the real application through
/// <c>WebApplicationFactory</c> rather than a rebuilt approximation of it.
/// </summary>
public partial class Program;
