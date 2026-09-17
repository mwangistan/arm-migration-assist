namespace Validation.Api;

public static class Problems
{
    public static IResult BadRequest(string detail) => Results.Problem(detail, statusCode: StatusCodes.Status400BadRequest);
    public static IResult NotFound(string detail) => Results.Problem(detail, statusCode: StatusCodes.Status404NotFound);
    public static IResult Conflict(string detail) => Results.Problem(detail, statusCode: StatusCodes.Status409Conflict);

    // Failed/cancelled are terminal: a report/dashboard will never become available for this
    // run, unlike queued/running where polling again may eventually succeed. The message must
    // say so plainly instead of implying the artifact merely isn't ready yet.
    public static IResult TerminalOrPendingConflict(ValidationRunRecord record, string artifactName) => record.Status switch
    {
        ValidationRunStatus.Failed => Conflict(
            $"Validation run failed; no {artifactName} is available." + (string.IsNullOrWhiteSpace(record.Error) ? "" : $" Error: {record.Error}")),
        ValidationRunStatus.Cancelled => Conflict($"Validation run was cancelled; no {artifactName} is available."),
        _ => Conflict($"Validation {artifactName} is available only after the run completes.")
    };
}

// Input validation is handled at the request boundary. An exception escaping that boundary
// (including InvalidDataException from persisted JSON) is an internal failure.
public static class ExceptionMapping
{
    public const string GenericServerErrorDetail = "An unexpected validation API error occurred.";

    public static (int Status, string Detail) Classify(Exception? error) =>
        (StatusCodes.Status500InternalServerError, GenericServerErrorDetail);
}
