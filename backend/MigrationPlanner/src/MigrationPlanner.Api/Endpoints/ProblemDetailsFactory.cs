using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MigrationPlanner.Api.Endpoints;

internal static class PlannerProblemDetailsFactory
{
    private const string ProblemTypePrefix = "https://arm-migration-assist.dev/problems/";

    public static IResult Problem(int statusCode, string code, string title, IEnumerable<string> details)
    {
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Type = ProblemTypePrefix + code,
            Detail = string.Join(" | ", details),
        };

        problem.Extensions["code"] = code;
        problem.Extensions["errors"] = details.ToArray();

        return Results.Problem(problem);
    }
}
