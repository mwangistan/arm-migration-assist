using System.Text;

namespace AutomatedMigration.Generators;

// Minimal unified-diff builder for small, targeted changes. Enough for a
// hackathon: a single-line replacement, or a whole new file.
public static class UnifiedDiff
{
    public static string ReplaceLine(string relativePath, IReadOnlyList<string> lines, int index, string newLine)
    {
        const int context = 3;
        int start = Math.Max(0, index - context);
        int end = Math.Min(lines.Count - 1, index + context);
        int span = end - start + 1;

        var sb = new StringBuilder();
        sb.Append("--- a/").Append(relativePath).Append('\n');
        sb.Append("+++ b/").Append(relativePath).Append('\n');
        sb.Append("@@ -").Append(start + 1).Append(',').Append(span)
          .Append(" +").Append(start + 1).Append(',').Append(span).Append(" @@\n");

        for (int i = start; i <= end; i++)
        {
            if (i == index)
            {
                sb.Append('-').Append(lines[i]).Append('\n');
                sb.Append('+').Append(newLine).Append('\n');
            }
            else
            {
                sb.Append(' ').Append(lines[i]).Append('\n');
            }
        }

        return sb.ToString();
    }

    public static string NewFile(string relativePath, string content)
    {
        var lines = content.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        var sb = new StringBuilder();
        sb.Append("--- /dev/null\n");
        sb.Append("+++ b/").Append(relativePath).Append('\n');
        sb.Append("@@ -0,0 +1,").Append(lines.Length).Append(" @@\n");
        foreach (var line in lines)
            sb.Append('+').Append(line).Append('\n');
        return sb.ToString();
    }
}
