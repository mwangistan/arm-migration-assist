using System.Text;
using MigrationPlanner.Application.Reporting;

namespace MigrationPlanner.Infrastructure.Reporting;

internal sealed class MarkdownReportRenderer : IMigrationReportRenderer
{
    public ReportFormat Format => ReportFormat.Markdown;
    public string ContentType => "text/markdown; charset=utf-8";
    public string FileExtension => "md";

    public Task<Stream> RenderAsync(MigrationReport report, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var plan = ReportRendering.ReadPlan(report);
        var root = plan.RootElement;
        var content = new StringBuilder()
            .AppendLine($"# {ReportRendering.Markdown(report.Repository.Name)} migration report")
            .AppendLine()
            .AppendLine($"- Repository: {ReportRendering.Markdown(report.Repository.Url)}")
            .AppendLine($"- Commit: `{report.Repository.CommitSha}`")
            .AppendLine($"- Branch: `{ReportRendering.Markdown(report.Repository.DefaultBranch)}`")
            .AppendLine($"- Run: `{report.RunId}`")
            .AppendLine($"- Generated: {ReportRendering.ReadString(root, "generatedAt")}")
            .AppendLine()
            .AppendLine("## Verdict")
            .AppendLine()
            .AppendLine($"**{report.Score.OverallScore}/100 - {ReportRendering.Markdown(report.Score.Band.ToString())}**")
            .AppendLine()
            .AppendLine($"Recommended path: **{ReportRendering.Markdown(ReportRendering.ReadString(root, "recommendedPath"))}** ")
            .AppendLine($"Confidence: **{ReportRendering.Markdown(ReportRendering.ReadString(root, "confidence"))}**")
            .AppendLine()
            .AppendLine("## Executive summary")
            .AppendLine()
            .AppendLine(ReportRendering.ReadString(root, "executiveSummary"))
            .AppendLine()
            .AppendLine("## Readiness dimensions")
            .AppendLine()
            .AppendLine("| Dimension | Score | Weight | Contribution |")
            .AppendLine("|---|---:|---:|---:|");

        foreach (var dimension in report.Score.Dimensions)
        {
            content.AppendLine($"| {ReportRendering.Markdown(dimension.DimensionKey.ToString())} | {dimension.RawScore} | {dimension.WeightPct}% | {dimension.WeightedContribution:0.##} |");
        }

        content.AppendLine()
            .AppendLine("## Migration work");
        foreach (var item in ReportRendering.ReadArray(root, "workItems"))
        {
            content.AppendLine()
                .AppendLine($"### {item.GetProperty("sequence").GetInt32()}. {ReportRendering.Markdown(ReportRendering.ReadString(item, "title"))}")
                .AppendLine()
                .AppendLine($"- Priority: {ReportRendering.ReadString(item, "priority")}")
                .AppendLine($"- Skill: `{ReportRendering.ReadString(item, "agentOrSkill")}`")
                .AppendLine($"- Approval required: {item.GetProperty("approvalRequired").GetBoolean()}")
                .AppendLine($"- Evidence: {ReportRendering.JoinStrings(item, "evidenceIds")}")
                .AppendLine()
                .AppendLine(ReportRendering.ReadString(item, "objective"));

            var tests = ReportRendering.ReadArray(item, "acceptanceTests").ToArray();
            if (tests.Length > 0)
            {
                content.AppendLine().AppendLine("Acceptance criteria:");
                foreach (var test in tests)
                {
                    content.AppendLine($"- {ReportRendering.ReadString(test, "description")} Expected: {ReportRendering.ReadString(test, "expectedOutcome")}");
                }
            }
        }

        if (report.Warnings.Count > 0)
        {
            content.AppendLine().AppendLine("## Warnings").AppendLine();
            foreach (var warning in report.Warnings)
            {
                content.AppendLine($"- {ReportRendering.Markdown(warning)}");
            }
        }

        content.AppendLine()
            .AppendLine("## Appendix")
            .AppendLine()
            .AppendLine("### Plan JSON")
            .AppendLine("```json")
            .AppendLine(ReportRendering.Serialize(report.Plan))
            .AppendLine("```")
            .AppendLine()
            .AppendLine("### Score JSON")
            .AppendLine("```json")
            .AppendLine(ReportRendering.Serialize(report.Score))
            .AppendLine("```")
            .AppendLine()
            .AppendLine($"Score digest: `{ReportRendering.ReadString(root, "scoreDigest")}`");

        return Task.FromResult<Stream>(ReportRendering.Stream(content));
    }
}
