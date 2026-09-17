# PyTorch on Windows on Arm — install status and GPU-parity gap

PyTorch publishes CPU-only Windows-on-Arm (`win_arm64`) wheels for `torch`, `torchvision`, and `torchaudio` on recent releases. The install path for a Windows ARM64 machine is the CPU index from pytorch.org — the default `pip install torch` may resolve to a source distribution and attempt a multi-hour build if the wheel index is not selected explicitly.

## What works

- `torch` CPU tensor ops, autograd, `torch.compile` (with CPU backend), TorchScript.
- `torchvision` image transforms and CPU-executed models.
- `torchaudio` CPU signal processing.
- `torch-directml` (Microsoft-maintained) provides a DirectML backend that reaches the Snapdragon X GPU on Windows on Arm for the subset of ops it implements. Coverage is smaller than CUDA and varies by model.

## What does not work

- **CUDA is not available on Windows on Arm.** No CUDA runtime, no cuDNN, no nightly CUDA build. Any code path that calls `.cuda()`, checks `torch.cuda.is_available()` and takes a CUDA branch, or imports a CUDA-only kernel package (`bitsandbytes`, `flash-attn`, `xformers` with CUDA kernels, etc.) will run only on CPU or fail.
- **Nightly builds and preview channels may lag the `win_arm64` release channel.** A workload that pins a nightly for a specific feature can lose ARM64 availability across a version.
- **Third-party PyTorch-adjacent packages with CUDA-only wheels have no ARM64 install path.** Examples: any inference stack that ships prebuilt CUDA kernels rather than pure PyTorch.

## Implications for planning

- A migration plan for a PyTorch workload on Windows-on-Arm must call out the CUDA gap explicitly. Do not promise "GPU parity with x64+CUDA" for anything that runs on Windows on Arm today.
- Two viable paths for GPU-class throughput on WoA: `torch-directml` (for supported ops) or exporting the model to ONNX and running inference through ONNX Runtime with the QNN execution provider on Snapdragon X. Neither is a drop-in replacement for a CUDA training loop.
- If the workload is inference-only, propose an ONNX Runtime fallback (see `onnxruntime-arm64-01`) instead of trying to close the CUDA gap in-place.

## Recommended repo-level pattern

- Split the install matrix so `win_arm64` uses the pytorch.org CPU wheel index explicitly, e.g. a separate install command in the ARM64 CI job.
- Guard CUDA-only imports behind `torch.cuda.is_available()` and provide a CPU fallback or a clear error message that names the missing platform.
- In `requirements-win-arm64.txt`, pin `torch`, `torchvision`, `torchaudio` to versions with confirmed `win_arm64` wheels on the target minor Python version.

## See also

- PyTorch documentation: <https://pytorch.org/>
