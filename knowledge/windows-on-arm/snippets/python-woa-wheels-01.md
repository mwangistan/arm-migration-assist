# Platform-specificity of Python packages on Windows on Arm

Python packages are distributed as either pure-Python wheels or platform-specific (binary) wheels. Pure-Python wheels install on any interpreter, on any OS, on any CPU architecture. Binary wheels contain compiled C, C++, Rust, or Fortran code and must match the target `(python-version, os, cpu-arch)` triple exactly. On Windows on Arm the target triple is `cp3XX-win_arm64`.

## Why this matters for migration

A `pip install -r requirements.txt` that works cleanly on `win_amd64` will not automatically work on `win_arm64`. For every package that ships compiled extensions, one of three things happens:

1. The package publishes a `win_arm64` wheel on PyPI. `pip` downloads it. Native ARM64 speed, no toolchain needed on the host.
2. The package publishes no `win_arm64` wheel but the source distribution builds cleanly. `pip` falls back to building from source — this requires the Microsoft Visual C++ Build Tools (and, for many modern packages, a Rust toolchain via `rustup`) installed on the ARM64 host, plus any package-specific system dependencies (e.g. `cmake`, `ninja`). Cold-installs become minutes-to-tens-of-minutes instead of seconds.
3. The package publishes no `win_arm64` wheel and cannot be built from source on Windows ARM64 (missing platform code, unmaintained dependency, or x64-only assumptions). Installation fails. The workload is blocked until an alternative, a patched fork, or a maintainer PR lands.

## Common offenders

Packages with compiled extensions to check for ARM64 wheel availability before planning a migration: `numpy`, `scipy`, `pillow`, `opencv-python`, `torch`, `torchvision`, `torchaudio`, `sentencepiece`, `tokenizers`, `cryptography`, `grpcio`, `protobuf`, `lxml`, `psycopg2`, `pyarrow`, `onnx`, `onnxruntime`. This list is illustrative, not exhaustive — always check PyPI for the actual `win_arm64` file listing per package version.

## Not applicable on Windows on Arm

`piwheels` is a Raspberry Pi index and only serves `linux_armv6l` / `linux_armv7l` / `linux_aarch64` wheels. It does not help Windows on Arm. Do not propose an alternate index URL that only hosts Linux ARM wheels.

## Recommended repo-level pattern

- Split requirements: keep a `requirements-common.txt` for pure-Python packages, and a `requirements-win-arm64.txt` that pins versions with confirmed `win_arm64` wheels.
- Pin exact versions (not ranges) for any package with compiled extensions — a lower minor bump may drop the ARM64 wheel.
- In CI, cache pip's wheel directory keyed by `(python-version, os, arch, requirements-hash)` so ARM64 source builds don't run on every commit.
- Document the required host toolchain (MSVC Build Tools, Rust) in the repo README so a fresh clone on an ARM64 machine has a repeatable install path.

## See also

- Arm Learning Paths: <https://learn.arm.com/learning-paths/laptops-and-desktops/win_python/how-to-1/>
