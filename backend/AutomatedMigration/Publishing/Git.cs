using System.Diagnostics;

namespace AutomatedMigration.Publishing;

// Thin wrapper around external processes (git, gh). Throws with captured output
// on failure so callers get a clear error.
public static class Git
{
    public static string Run(string cwd, params string[] args) => RunTool(cwd, "git", args);

    public static string RunTool(string cwd, string file, params string[] args)
    {
        var psi = new ProcessStartInfo(file)
        {
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {file}");
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"{file} {string.Join(' ', args)} failed:\n{stderr}{stdout}");
        return stdout;
    }

    public static bool ToolExists(string file)
    {
        try { RunTool(Environment.CurrentDirectory, file, "--version"); return true; }
        catch { return false; }
    }
}
