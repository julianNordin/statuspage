namespace StatusPage.Checker;

/// <summary>
/// Log messages for the host itself. Source-generated rather than interpolated, because the
/// analyzers insist and because a generated message keeps its named properties in the
/// structured output instead of flattening them into a string.
/// </summary>
internal static partial class CheckerLog
{
    [LoggerMessage(EventId = 3100, Level = LogLevel.Error, Message = "The check cycle failed")]
    public static partial void CycleFailed(ILogger logger, Exception exception);
}
