using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace StatusPage.Api.Infrastructure;

/// <summary>
/// Makes every <see cref="ProblemDetails"/> response say it is one.
/// <para>
/// The bodies MVC produces for validation failures and for <c>Problem(...)</c> are already
/// correct RFC 9457 documents, and the results even carry
/// <c>application/problem+json</c> in their <c>ContentTypes</c> — but the JSON output
/// formatter matches that against its own <c>application/*+json</c> wildcard and reports the
/// concrete type it prefers, which is <c>application/json</c>. Adding the exact media type to
/// the formatter's supported list does not change the outcome; it was measured.
/// </para>
/// <para>
/// So the body is written here instead, where the content type is stated rather than
/// negotiated. A client that branches on the media type — the entire reason RFC 9457
/// registers one — can then tell an error from a payload.
/// </para>
/// </summary>
internal sealed class ProblemDetailsContentTypeFilter(IOptions<JsonOptions> jsonOptions) : IResultFilter
{
    private const string ProblemJson = "application/problem+json";

    public void OnResultExecuting(ResultExecutingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Result is not ObjectResult { Value: ProblemDetails problem } result)
        {
            return;
        }

        context.Result = new ContentResult
        {
            StatusCode = result.StatusCode ?? problem.Status ?? StatusCodes.Status500InternalServerError,
            ContentType = ProblemJson,
            Content = JsonSerializer.Serialize(
                problem, problem.GetType(), jsonOptions.Value.JsonSerializerOptions),
        };
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
        // Nothing to do once it has been written.
    }
}
