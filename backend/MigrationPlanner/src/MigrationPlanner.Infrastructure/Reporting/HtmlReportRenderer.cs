using System.Text;
using MigrationPlanner.Application.Reporting;

namespace MigrationPlanner.Infrastructure.Reporting;

internal sealed class HtmlReportRenderer : IMigrationReportRenderer
{
    public ReportFormat Format => ReportFormat.Html;
    public string ContentType => "text/html; charset=utf-8";
    public string FileExtension => "html";

    public Task<Stream> RenderAsync(MigrationReport report, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var plan = ReportRendering.ReadPlan(report);
        var root = plan.RootElement;
        var title = ReportRendering.Html(report.Repository.Name);
        var content = new StringBuilder($$$"""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width">
            <title>{{{title}}} migration report</title>
            <style>body{font:14px/1.55 Segoe UI,Arial,sans-serif;color:#242424;max-width:1050px;margin:36px auto;padding:0 24px}h1,h2,h3{color:#1b1a19}header{border-bottom:3px solid #0067b8;padding-bottom:18px}.meta{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:6px 20px;color:#605e5c}.verdict{display:grid;grid-template-columns:180px 1fr;border-block:1px solid #d6d9dd;margin:28px 0}.score{font-size:42px;font-weight:650;padding:22px 0}.summary{padding:22px 0 22px 28px}table{width:100%;border-collapse:collapse}th,td{text-align:left;border-bottom:1px solid #e8eaed;padding:8px}.item{break-inside:avoid;border-top:1px solid #d6d9dd;padding:16px 0}.tag{display:inline-block;background:#f0f6fa;border-left:3px solid #0067b8;padding:3px 7px;margin-right:6px}pre{white-space:pre-wrap;overflow-wrap:anywhere;background:#f5f7f9;padding:14px;font-size:11px}@media print{body{margin:0;max-width:none}.item,tr{break-inside:avoid}}</style>
            </head><body><header><h1>{{{title}}} migration report</h1><div class="meta">
            <span>Repository: {{{ReportRendering.Html(report.Repository.Url)}}}</span><span>Commit: {{{ReportRendering.Html(report.Repository.CommitSha)}}}</span>
            <span>Branch: {{{ReportRendering.Html(report.Repository.DefaultBranch)}}}</span><span>Run: {{{ReportRendering.Html(report.RunId)}}}</span>
            </div></header><main><section class="verdict"><div class="score">{{{report.Score.OverallScore}}}/100</div><div class="summary">
            <h2>{{{ReportRendering.Html(ReportRendering.ReadString(root, "recommendedPath"))}}}</h2><p>{{{ReportRendering.Html(ReportRendering.ReadString(root, "executiveSummary"))}}}</p>
            </div></section><section><h2>Readiness dimensions</h2><table><thead><tr><th>Dimension</th><th>Score</th><th>Weight</th><th>Contribution</th></tr></thead><tbody>
            """);

        foreach (var dimension in report.Score.Dimensions)
        {
            content.Append($"<tr><td>{ReportRendering.Html(dimension.DimensionKey.ToString())}</td><td>{dimension.RawScore}</td><td>{dimension.WeightPct}%</td><td>{dimension.WeightedContribution:0.##}</td></tr>");
        }

        content.Append("</tbody></table></section><section><h2>Migration work</h2>");
        foreach (var item in ReportRendering.ReadArray(root, "workItems"))
        {
            content.Append("<article class=\"item\"><div>")
                .Append($"<span class=\"tag\">{ReportRendering.Html(ReportRendering.ReadString(item, "priority"))}</span>")
                .Append($"<span class=\"tag\">{ReportRendering.Html(ReportRendering.ReadString(item, "agentOrSkill"))}</span></div>")
                .Append($"<h3>{item.GetProperty("sequence").GetInt32()}. {ReportRendering.Html(ReportRendering.ReadString(item, "title"))}</h3>")
                .Append($"<p>{ReportRendering.Html(ReportRendering.ReadString(item, "objective"))}</p>");
            var tests = ReportRendering.ReadArray(item, "acceptanceTests").ToArray();
            if (tests.Length > 0)
            {
                content.Append("<h4>Acceptance criteria</h4><ul>");
                foreach (var test in tests)
                {
                    content.Append($"<li>{ReportRendering.Html(ReportRendering.ReadString(test, "description"))} Expected: {ReportRendering.Html(ReportRendering.ReadString(test, "expectedOutcome"))}</li>");
                }
                content.Append("</ul>");
            }
            content.Append("</article>");
        }

        content.Append("</section><section><h2>Appendix</h2><h3>Plan JSON</h3><pre>")
            .Append(ReportRendering.Html(ReportRendering.Serialize(report.Plan)))
            .Append("</pre><h3>Score JSON</h3><pre>")
            .Append(ReportRendering.Html(ReportRendering.Serialize(report.Score)))
            .Append("</pre></section></main><footer>Score digest: <code>")
            .Append(ReportRendering.Html(ReportRendering.ReadString(root, "scoreDigest")))
            .Append("</code></footer></body></html>");

        return Task.FromResult<Stream>(ReportRendering.Stream(content));
    }
}
