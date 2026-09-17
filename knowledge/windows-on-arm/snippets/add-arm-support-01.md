# Add Arm support to your Windows app

Adding ARM64 support to an existing Windows application typically follows four broad stages: assess, plan, build, and validate.

## Assess

Inventory the application's language mix, build system, native dependencies, and platform-specific code. The main blockers are almost always native dependencies without ARM64 builds and architecture-specific code paths (SSE/AVX intrinsics, inline assembly, pointer-size assumptions, P/Invoke into x64-only libraries, or driver requirements).

## Plan

Choose a migration path based on evidence:

- **Native ARM64** when all required dependencies have ARM64 builds and code is portable. Best performance and power efficiency.
- **Arm64EC** when native dependencies or plug-ins force incremental migration.
- **Staged** when parts of the application can move to native ARM64 while other parts remain x64 emulated until dependencies are ready.

## Build

Update project files, build scripts, and CI to add ARM64 configurations. For MSBuild-based projects this means adding the ARM64 platform to the solution and each project. For CMake and other build systems, add ARM64 toolchain files or presets. Update packaging (MSIX, MSI, installer scripts) so an ARM64 artifact is produced.

## Validate

Run the ARM64 build on real ARM64 hardware. Measure startup time, sustained performance, power consumption, and functional coverage. Compare against the x64 build under emulation to quantify the benefit. Track functional regressions with clear reproducers.

## Common blockers

- x64-only native dependency without an ARM64 replacement.
- Kernel drivers or filter drivers without ARM64 signing.
- Architecture-specific code with no fallback path.
- Installer or packaging without an ARM64 code path.
- CI pipelines with no ARM64 build or test job.
