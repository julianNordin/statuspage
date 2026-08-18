using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Diagnostics;
using StatusPage.Checker.Probing;
using StatusPage.Domain;
using StatusPage.Infrastructure.ReadModel;

namespace StatusPage.Checker.Tests;

/// <summary>
/// A probe that answers whatever the test says. Everything interesting about the cycle is a
/// function of outcomes, and outcomes are far easier to arrange than servers.
/// </summary>
internal sealed class ScriptedProbe : ITargetProbe
{
    public CheckOutcome Default { get; set; } = CheckOutcome.Responded(200, TimeSpan.FromMilliseconds(10));

    public int Calls { get; private set; }

    public Task<CheckOutcome> ProbeAsync(string targetUrl, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(Default);
    }
}

/// <summary>
/// The read model in a dictionary. Round-trips through JSON on purpose: a document that cannot
/// survive serialisation is broken in production and fine in a test that stores the object.
/// </summary>
internal sealed class InMemoryReadModelStore : IReadModelStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<string, string> _documents = new(StringComparer.Ordinal);

    public int Reads { get; private set; }

    public int Writes { get; private set; }

    public Task<T?> ReadAsync<T>(string name, CancellationToken cancellationToken = default) where T : class
    {
        Reads++;
        return Task.FromResult(_documents.TryGetValue(name, out var json)
            ? JsonSerializer.Deserialize<T>(json, Json)
            : null);
    }

    public Task WriteAsync<T>(string name, T document, CancellationToken cancellationToken = default)
        where T : class
    {
        Writes++;
        _documents[name] = JsonSerializer.Serialize(document, Json);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Counts SQL commands as they are executed.
/// <para>
/// The claim this phase rests on is "a quiet cycle touches no database". A boolean returned by
/// the code under test would only be reporting its own opinion; this counts what actually
/// reached the connection.
/// </para>
/// </summary>
public sealed class CommandCounter : DbCommandInterceptor
{
    public int Commands { get; private set; }

    public void Reset() => Commands = 0;

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Commands++;
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Commands++;
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Commands++;
        return base.NonQueryExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Commands++;
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Commands++;
        return base.ScalarExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        Commands++;
        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }
}

/// <summary>
/// A clock the test moves by hand. Every rule in this project takes its time from an injected
/// TimeProvider, which turns an eight-cycle outage into microseconds.
/// </summary>
internal sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);
}
