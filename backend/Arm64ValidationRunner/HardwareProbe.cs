using System.Runtime.InteropServices;

namespace Arm64ValidationRunner;

public static class HardwareProbe
{
    public static HardwareInfo Snapshot(string vmSku, string region)
    {
        var arch = RuntimeInformation.ProcessArchitecture.ToString();
        var kernel = RuntimeInformation.OSDescription;
        var (cpuModel, cpuCount) = ProbeCpu();
        var memoryMB = ProbeMemoryMb();
        return new HardwareInfo(vmSku, region, arch, kernel, cpuModel, cpuCount, memoryMB);
    }

    private static (string Model, int Count) ProbeCpu()
    {
        try
        {
            var lines = File.ReadAllLines("/proc/cpuinfo");
            var model = lines.FirstOrDefault(l => l.StartsWith("model name", StringComparison.Ordinal))?.Split(':', 2).Last().Trim()
                ?? lines.FirstOrDefault(l => l.StartsWith("CPU implementer", StringComparison.Ordinal))?.Split(':', 2).Last().Trim()
                ?? "unknown";
            var count = lines.Count(l => l.StartsWith("processor", StringComparison.Ordinal));
            return (model, count > 0 ? count : Environment.ProcessorCount);
        }
        catch
        {
            return ("unknown", Environment.ProcessorCount);
        }
    }

    private static long ProbeMemoryMb()
    {
        try
        {
            foreach (var line in File.ReadAllLines("/proc/meminfo"))
            {
                if (!line.StartsWith("MemTotal:", StringComparison.Ordinal)) continue;
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && long.TryParse(parts[1], out var kb))
                {
                    return kb / 1024;
                }
            }
        }
        catch { /* fall through */ }
        return 0;
    }
}
