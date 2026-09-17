using System.Text;

namespace AutomatedMigration.Generators;

// Minimal unified-diff builder for small, targeted changes. Emits patches using
// the target file's own line endings so the result applies cleanly on both LF
// and CRLF repositories.
public static class UnifiedDiff
{
    public static string DetectNewline(string text) => text.Contains("\r\n") ? "\r\n" : "\n";

    public static IReadOnlyList<string> SplitLines(string text)
    {
        var normalized = text.Replace("\r\n", "\n");
        if (normalized.EndsWith('\n'))
            normalized = normalized[..^1];
        return normalized.Split('\n');
    }

    public static string ReplaceLine(string relativePath, IReadOnlyList<string> lines, int index, string newLine, string nl = "\n")
    {
        const int context = 3;
        int start = Math.Max(0, index - context);
        int end = Math.Min(lines.Count - 1, index + context);
        int span = end - start + 1;

        var sb = new StringBuilder();
        sb.Append("--- a/").Append(relativePath).Append(nl);
        sb.Append("+++ b/").Append(relativePath).Append(nl);
        sb.Append("@@ -").Append(start + 1).Append(',').Append(span)
          .Append(" +").Append(start + 1).Append(',').Append(span).Append(" @@").Append(nl);

        for (int i = start; i <= end; i++)
        {
            if (i == index)
            {
                sb.Append('-').Append(lines[i]).Append(nl);
                sb.Append('+').Append(newLine).Append(nl);
            }
            else
            {
                sb.Append(' ').Append(lines[i]).Append(nl);
            }
        }

        return sb.ToString();
    }

    public static string InsertAfter(string relativePath, IReadOnlyList<string> lines, int index, string insertion, string nl = "\n")
    {
        var inserted = insertion.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        const int context = 3;
        int start = Math.Max(0, index - context);
        int end = Math.Min(lines.Count - 1, index + context);
        int oldCount = end - start + 1;
        int newCount = oldCount + inserted.Length;

        var sb = new StringBuilder();
        sb.Append("--- a/").Append(relativePath).Append(nl);
        sb.Append("+++ b/").Append(relativePath).Append(nl);
        sb.Append("@@ -").Append(start + 1).Append(',').Append(oldCount)
          .Append(" +").Append(start + 1).Append(',').Append(newCount).Append(" @@").Append(nl);

        for (int i = start; i <= end; i++)
        {
            sb.Append(' ').Append(lines[i]).Append(nl);
            if (i == index)
            {
                foreach (var ins in inserted)
                    sb.Append('+').Append(ins).Append(nl);
            }
        }

        return sb.ToString();
    }

    public static string NewFile(string relativePath, string content, string nl = "\n")
    {
        var lines = content.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        var sb = new StringBuilder();
        sb.Append("--- /dev/null").Append(nl);
        sb.Append("+++ b/").Append(relativePath).Append(nl);
        sb.Append("@@ -0,0 +1,").Append(lines.Length).Append(" @@").Append(nl);
        foreach (var line in lines)
            sb.Append('+').Append(line).Append(nl);
        return sb.ToString();
    }
}
