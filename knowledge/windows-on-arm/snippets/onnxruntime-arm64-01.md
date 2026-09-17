# ONNX Runtime on Windows on Arm — the primary native-ARM64 inference path

ONNX Runtime publishes native `win_arm64` wheels and a Windows on Arm binary distribution. It runs models natively on ARM64 CPUs and, on Snapdragon X-series devices, offers the QNN execution provider to reach the Hexagon NPU. This makes ONNX Runtime the default answer for the CUDA-not-available problem on Windows on Arm (see `pytorch-woa-status-01`).

## When to propose an ONNX Runtime fallback

- The workload does inference on models that started life in PyTorch or TensorFlow.
- GPU parity with x64+CUDA is required on Windows on Arm, and `torch-directml` coverage is not sufficient for the model's ops.
- The model shape is stable (no per-request graph edits) — good ONNX export target.

## Migration shape

Two-step pattern that keeps the training pipeline unchanged:

1. **Export.** Convert the trained model to ONNX once, offline, on the existing x64+CUDA training host, using `torch.onnx.export(...)` (or the framework's native ONNX exporter). Commit the `.onnx` artifact to the release, not to the source tree.
2. **Serve on ARM64.** Load the ONNX file at runtime with the `onnxruntime` Python package (or the `Microsoft.ML.OnnxRuntime` .NET package). Select an execution provider based on host capability at startup:

    ```python
    import onnxruntime as ort

    available = ort.get_available_providers()
    # Order matters — first supported provider wins.
    preferred = ["QNNExecutionProvider", "CPUExecutionProvider"]
    providers = [p for p in preferred if p in available]
    session = ort.InferenceSession("model.onnx", providers=providers)
    ```

## What to warn about in the plan

- **Not every model exports cleanly.** Custom `autograd.Function`, control flow that depends on tensor values, dynamic shapes beyond ONNX's supported dynamic axes, and third-party ops without an ONNX equivalent all break `torch.onnx.export`. Budget time for op-level rewrites.
- **Numerical drift.** Exported models are usually numerically equivalent to the source within tolerance, but not bit-identical. Any tests that assert exact output values must be updated with a tolerance or replaced with property-based tests.
- **QNN provider is device-specific.** It only applies on Snapdragon X-family NPUs. On other Windows-on-Arm hardware the runtime falls back to the CPU provider. Do not promise NPU acceleration for arbitrary WoA hardware.
- **License and packaging.** The `.onnx` artifact must be shipped with the app or fetched at startup. Update packaging and installers to include or download it.

## See also

- ONNX Runtime documentation: <https://onnxruntime.ai/>
