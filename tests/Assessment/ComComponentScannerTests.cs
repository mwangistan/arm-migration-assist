using ArmMigrationAssist.Api.Assessment;
using ArmMigrationAssist.Api.Assessment.DependencyScanner;

namespace ArmMigrationAssist.Tests;

/// <summary>
/// Tests for COM component detection (Story 1.3 "Identify COM components"). The signal detector is
/// pure text-in / signals-out, and the working-tree scan is exercised against a temp directory.
/// </summary>
public sealed class ComComponentScannerTests
{
    [Fact]
    public void Detects_DotNet_ProgId_Activation()
    {
        const string code = """
            var t = Type.GetTypeFromProgID("Excel.Application");
            var app = Activator.CreateInstance(t);
            """;
        var signals = ComComponentScanner.DetectSignals(code).ToList();
        Assert.Contains(signals, s => s.Name == "Excel.Application" && s.Category == "ProgID activation");
    }

    [Fact]
    public void Detects_Vb_CreateObject_ProgId()
    {
        const string code = "Set fso = CreateObject(\"Scripting.FileSystemObject\")";
        var signals = ComComponentScanner.DetectSignals(code).ToList();
        Assert.Contains(signals, s => s.Name == "Scripting.FileSystemObject");
    }

    [Fact]
    public void Detects_JScript_ActiveXObject()
    {
        const string code = "var shell = new ActiveXObject('WScript.Shell');";
        var signals = ComComponentScanner.DetectSignals(code).ToList();
        Assert.Contains(signals, s => s.Name == "WScript.Shell" && s.Category == "ActiveX activation");
    }

    [Fact]
    public void Detects_Clsid_Activation()
    {
        const string code = """
            var t = Type.GetTypeFromCLSID(new Guid("000209FF-0000-0000-C000-000000000046"));
            """;
        var signals = ComComponentScanner.DetectSignals(code).ToList();
        Assert.Contains(signals, s => s.Category == "CLSID activation" &&
                                      s.Name.Contains("000209FF", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Detects_Cpp_CoCreateInstance_With_Clsid_Token()
    {
        const string code = "hr = CoCreateInstance(CLSID_FilgraphManager, NULL, CLSCTX_INPROC_SERVER, IID_IMediaControl, (void**)&pmc);";
        var signals = ComComponentScanner.DetectSignals(code).ToList();
        Assert.Contains(signals, s => s.Category == "COM activation (C/C++)" &&
                                      s.Name.Contains("CLSID_FilgraphManager"));
    }

    [Fact]
    public void Detects_DotNet_ComImport_Type()
    {
        const string code = """
            [ComImport]
            [Guid("00020970-0000-0000-C000-000000000046")]
            interface IWordApplication { }
            """;
        var signals = ComComponentScanner.DetectSignals(code).ToList();
        Assert.Contains(signals, s => s.Category == ".NET COM interop" &&
                                      s.Name == "COM interop type IWordApplication");
    }

    [Fact]
    public void Detects_Project_ComReference()
    {
        const string xml = """
            <ItemGroup>
              <COMReference Include="Microsoft.Office.Interop.Excel">
                <Guid>{00020813-0000-0000-C000-000000000046}</Guid>
              </COMReference>
            </ItemGroup>
            """;
        var signals = ComComponentScanner.DetectSignals(xml).ToList();
        Assert.Contains(signals, s => s.Category == "Project COM reference" &&
                                      s.Name == "Microsoft.Office.Interop.Excel");
    }

    [Fact]
    public void Detects_Regsvr32_Registration()
    {
        const string script = "regsvr32 /s MyControl.ocx";
        var signals = ComComponentScanner.DetectSignals(script).ToList();
        Assert.Contains(signals, s => s.Category == "COM registration (regsvr32)" && s.Name == "MyControl.ocx");
    }

    [Fact]
    public void Detects_Cpp_Import_TypeLibrary()
    {
        const string code = "#import \"mscorlib.tlb\" raw_interfaces_only";
        var signals = ComComponentScanner.DetectSignals(code).ToList();
        Assert.Contains(signals, s => s.Category == "Type-library import" && s.Name == "mscorlib.tlb");
    }

    [Fact]
    public void NonCom_Code_Yields_No_Signals()
    {
        const string code = """
            public class Foo { public int Bar() => 42; }
            var list = new List<int>();
            """;
        Assert.Empty(ComComponentScanner.DetectSignals(code));
    }

    [Fact]
    public void Reports_Line_Numbers()
    {
        const string code = "line one\nline two\nvar t = Type.GetTypeFromProgID(\"Excel.Application\");\n";
        var signal = Assert.Single(ComComponentScanner.DetectSignals(code));
        Assert.Equal(3, signal.Line);
    }

    [Fact]
    public void Scan_Produces_Unknown_Com_Findings_From_Working_Tree()
    {
        var dir = Directory.CreateTempSubdirectory("com-scan-");
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "Program.cs"),
                "var t = Type.GetTypeFromProgID(\"Excel.Application\");");

            var findings = ComComponentScanner.Scan(dir.FullName);

            var excel = Assert.Single(findings);
            Assert.Equal("com", excel.Source);
            Assert.Equal("Excel.Application", excel.Name);
            Assert.Equal(DependencyClassification.Unknown, excel.Classification);
            Assert.Equal("Program.cs", excel.EvidencePath);
            Assert.Contains("ARM64", excel.Notes);
        }
        finally { dir.Delete(recursive: true); }
    }

    [Fact]
    public void Scan_Dedupes_Same_Component_Across_Files()
    {
        var dir = Directory.CreateTempSubdirectory("com-scan-dedupe-");
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "A.cs"),
                "Type.GetTypeFromProgID(\"Excel.Application\");");
            File.WriteAllText(Path.Combine(dir.FullName, "B.cs"),
                "Type.GetTypeFromProgID(\"Excel.Application\");");

            var findings = ComComponentScanner.Scan(dir.FullName);

            Assert.Single(findings.Where(f => f.Name == "Excel.Application"));
        }
        finally { dir.Delete(recursive: true); }
    }
}
