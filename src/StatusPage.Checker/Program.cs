using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Formatting.Compact;
using StatusPage.Checker;
using StatusPage.Checker.Probing;
using StatusPage.Infrastructure;
using StatusPage.Infrastructure.Checks;

var builder = Host.CreateApplicationBuilder(args);

// Compact JSON to the console, because the only thing that reads these is a log platform.
// A correlation id is attached per run, below, so one cycle's lines can be pulled out of a
// day of them.
builder.Services.AddSerilog(configuration => configuration
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(new CompactJsonFormatter()));

builder.Services.AddDbContext<StatusPageDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("Default"),
        sql => sql.EnableRetryOnFailure()));

builder.Services.AddSingleton(TimeProvider.System);

// Every check goes out through the guarded handler: resolve, refuse anything private, then
// connect to an address that was actually checked.
builder.Services
    .AddHttpClient<ITargetProbe, HttpTargetProbe>(client =>
    {
        client.Timeout = Timeout.InfiniteTimeSpan;
        client.DefaultRequestHeaders.UserAgent.ParseAdd("statuspage-checker/1.0");
    })
    .ConfigurePrimaryHttpMessageHandler(() =>
        GuardedConnect.CreateHandler(TimeSpan.FromSeconds(5)));

builder.Services.AddScoped<CheckCycle>();

var host = builder.Build();

// One shot by default: run, write, exit. That is exactly what a Container Apps cron job
// wants, and a process that exits is far easier to test than a loop. Set Checker:Loop for
// local development, where watching it tick is the point.
var loop = builder.Configuration.GetValue<bool>("Checker:Loop");
var interval = TimeSpan.FromSeconds(builder.Configuration.GetValue("Checker:IntervalSeconds", 60));

var logger = host.Services.GetRequiredService<ILogger<Program>>();
var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();

do
{
    using var scope = host.Services.CreateScope();
    var correlationId = Guid.CreateVersion7().ToString("N");

    using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId))
    {
        try
        {
            var cycle = scope.ServiceProvider.GetRequiredService<CheckCycle>();
            await cycle.RunAsync(lifetime.ApplicationStopping).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (lifetime.ApplicationStopping.IsCancellationRequested)
        {
            // Asked to stop mid-cycle. Nothing partial was saved: the whole pass writes once,
            // at the end, so an interrupted run leaves the database exactly as it found it.
            break;
        }
        catch (Exception ex)
        {
            // A cycle that throws must not take the process down in loop mode, and must
            // report a non-zero exit code in one-shot mode so the platform sees the failure.
            CheckerLog.CycleFailed(logger, ex);

            if (!loop)
            {
                return 1;
            }
        }
    }

    if (loop)
    {
        try
        {
            await Task.Delay(interval, lifetime.ApplicationStopping).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            break;
        }
    }
}
while (loop && !lifetime.ApplicationStopping.IsCancellationRequested);

await Log.CloseAndFlushAsync().ConfigureAwait(false);
return 0;
