namespace MigrationPlanner.Application.Reporting;

public interface IMigrationReportRenderer
{
    ReportFormat Format { get; }

    string ContentType { get; }

    string FileExtension { get; }

    Task<Stream> RenderAsync(MigrationReport report, CancellationToken cancellationToken);
}
