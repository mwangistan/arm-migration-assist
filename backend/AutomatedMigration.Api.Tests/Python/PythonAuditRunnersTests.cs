using AutomatedMigration.CodeMigration.Python;
using AutomatedMigration.Generators;
using AutomatedMigration.Models;
using FluentAssertions;
using Xunit;

namespace AutomatedMigration.Api.Tests.Python;

public sealed class PythonAuditRunnersTests : IDisposable
{
    private readonly string _repo;

    public PythonAuditRunnersTests()
    {
        _repo = Path.Combine(Path.GetTempPath(), "arm-mig-python-audit-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(_repo);
    }

    public void Dispose()
    {
        try { Directory.Delete(_repo, recursive: true); } catch { }
    }

    private static WorkItem Item(string skill) => new(
        Id: "wi-test",
        Sequence: 1,
        Priority: "P0",
        Title: "t",
        Objective: "o",
        AgentOrSkill: skill,
        Inputs: null,
        ExpectedOutputs: null,
        Dependencies: null,
        EvidenceIds: null,
        GuidanceIds: null,
        AcceptanceTests: null,
        ApprovalRequired: true);

    private MigrationContext Context() =>
        new(_repo, new Dictionary<string, WorkItem>(StringComparer.Ordinal));

    // ----- pytorch-arm64-wheel-audit -----

    [Fact]
    public void PytorchAudit_returns_null_when_no_torch_declared()
    {
        File.WriteAllText(Path.Combine(_repo, "requirements.txt"), "numpy==1.26.4\n");
        var patch = new PyTorchArm64WheelAudit().Generate(Item("python/pytorch-arm64-wheel-audit"), Context());
        patch.Should().BeNull();
    }

    [Fact]
    public void PytorchAudit_emits_report_naming_each_torch_package()
    {
        File.WriteAllText(Path.Combine(_repo, "requirements.txt"),
            "torch==2.6.0\ntorchvision>=0.21\ntorchaudio\nnumpy\n");
        var patch = new PyTorchArm64WheelAudit().Generate(Item("python/pytorch-arm64-wheel-audit"), Context());
        patch.Should().NotBeNull();
        patch!.Diff.Should().Contain(".arm-migration/reports/pytorch-arm64-wheel-audit.md");
        patch.Diff.Should().Contain("`torch`");
        patch.Diff.Should().Contain("`torchvision`");
        patch.Diff.Should().Contain("`torchaudio`");
        patch.Diff.Should().Contain("pytorch-woa-status-01");
        patch.Diff.Should().NotContain("`numpy`");
    }

    [Fact]
    public void PytorchAudit_flags_unpinned_torch_for_reviewer()
    {
        File.WriteAllText(Path.Combine(_repo, "requirements.txt"), "torch\n");
        var patch = new PyTorchArm64WheelAudit().Generate(Item("python/pytorch-arm64-wheel-audit"), Context());
        patch.Should().NotBeNull();
        patch!.Diff.Should().Contain("Unpinned");
    }

    // ----- native-wheel-audit -----

    [Fact]
    public void NativeWheelAudit_returns_null_without_pip_manifest()
    {
        var patch = new PythonNativeWheelAudit().Generate(Item("python/native-wheel-audit"), Context());
        patch.Should().BeNull();
    }

    [Fact]
    public void NativeWheelAudit_classifies_known_native_and_torch_deps()
    {
        File.WriteAllText(Path.Combine(_repo, "requirements.txt"),
            "numpy==1.26.4\ntorch==2.6.0\nfilelock\nsome-obscure-package\n");
        var patch = new PythonNativeWheelAudit().Generate(Item("python/native-wheel-audit"), Context());
        patch.Should().NotBeNull();
        patch!.Diff.Should().Contain("Known native");
        patch.Diff.Should().Contain("Follows torch");
        patch.Diff.Should().Contain("python-woa-wheels-01");
    }

    [Fact]
    public void NativeWheelAudit_reads_pyproject_toml()
    {
        File.WriteAllText(Path.Combine(_repo, "pyproject.toml"),
            "[project]\nname = \"app\"\nversion = \"0.1\"\ndependencies = [\n  \"pillow>=10.4\",\n  \"pydantic\",\n]\n");
        var patch = new PythonNativeWheelAudit().Generate(Item("python/native-wheel-audit"), Context());
        patch.Should().NotBeNull();
        patch!.Diff.Should().Contain("`pillow`");
        patch.Diff.Should().Contain("`pydantic`");
    }

    // ----- cuda-to-directml-audit -----

    [Fact]
    public void CudaAudit_returns_null_when_source_has_no_cuda_references()
    {
        Directory.CreateDirectory(Path.Combine(_repo, "app"));
        File.WriteAllText(Path.Combine(_repo, "app", "main.py"), "print('hello')\n");
        var patch = new CudaToDirectMlAudit().Generate(Item("python/cuda-to-directml-audit"), Context());
        patch.Should().BeNull();
    }

    [Fact]
    public void CudaAudit_reports_torch_cuda_call_sites()
    {
        Directory.CreateDirectory(Path.Combine(_repo, "app"));
        File.WriteAllText(Path.Combine(_repo, "app", "model.py"),
            "import torch\n" +
            "def load():\n" +
            "    model = build_model().to(\"cuda\")\n" +
            "    return model.cuda()\n" +
            "def check():\n" +
            "    return torch.cuda.is_available()\n");
        var patch = new CudaToDirectMlAudit().Generate(Item("python/cuda-to-directml-audit"), Context());
        patch.Should().NotBeNull();
        patch!.Diff.Should().Contain("app/model.py");
        patch.Diff.Should().Contain("torch-directml");
        patch.Diff.Should().Contain("onnxruntime-arm64-01");
    }

    [Fact]
    public void CudaAudit_skips_common_vendored_dirs()
    {
        Directory.CreateDirectory(Path.Combine(_repo, ".venv", "lib"));
        File.WriteAllText(Path.Combine(_repo, ".venv", "lib", "bad.py"), "import torch\nx = torch.cuda.is_available()\n");
        var patch = new CudaToDirectMlAudit().Generate(Item("python/cuda-to-directml-audit"), Context());
        patch.Should().BeNull();
    }

    // ----- pip-constraints-arm64-scaffold -----

    [Fact]
    public void ConstraintsScaffold_returns_null_without_manifest()
    {
        var patch = new PipConstraintsArm64Scaffold().Generate(Item("python/pip-constraints-arm64-scaffold"), Context());
        patch.Should().BeNull();
    }

    [Fact]
    public void ConstraintsScaffold_emits_two_files_when_manifest_exists()
    {
        File.WriteAllText(Path.Combine(_repo, "requirements.txt"), "torch==2.6.0\nnumpy\n");
        var patch = new PipConstraintsArm64Scaffold().Generate(Item("python/pip-constraints-arm64-scaffold"), Context());
        patch.Should().NotBeNull();
        patch!.Diff.Should().Contain("constraints-arm64.txt");
        patch.Diff.Should().Contain(".arm-migration/reports/pip-constraints-arm64.md");
        patch.Diff.Should().Contain("# torch (currently ==2.6.0)");
        patch.Diff.Should().Contain("# numpy (currently unpinned)");
    }

    [Fact]
    public void ConstraintsScaffold_is_idempotent_when_output_already_exists()
    {
        File.WriteAllText(Path.Combine(_repo, "requirements.txt"), "torch\n");
        File.WriteAllText(Path.Combine(_repo, "constraints-arm64.txt"), "torch==2.6.0\n");
        var patch = new PipConstraintsArm64Scaffold().Generate(Item("python/pip-constraints-arm64-scaffold"), Context());
        patch.Should().BeNull();
    }
}
