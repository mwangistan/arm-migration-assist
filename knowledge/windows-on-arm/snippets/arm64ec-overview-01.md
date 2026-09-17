# Arm64EC overview

Arm64EC ("Emulation Compatible") is an ABI for Windows on Arm that lets a single process contain a mix of native Arm64EC code and x64 code running under emulation. This makes Arm64EC especially useful for large or plugin-heavy applications that cannot be recompiled to native ARM64 in one step.

Arm64EC binaries run natively on ARM64 hardware for the parts of the process that have been recompiled. Modules that remain x64 continue to run through the built-in x64 emulator. Calls across the boundary are handled by the Arm64EC ABI without changes to source code.

## When to choose Arm64EC

- The application has a large native codebase that cannot be recompiled all at once.
- The application supports third-party x64 plug-ins that must continue to load.
- Migration must happen incrementally, module by module, without breaking existing functionality.

## When native ARM64 is preferable

- The application is primarily managed code (.NET) or uses portable code with no x64-only dependencies.
- All required native components already have ARM64 builds available.
- Best-in-class performance and battery life are the priority.

## Constraints and considerations

- Arm64EC modules are still ARM64 code; only the ABI is compatible with x64 interop. They do not run under emulation themselves.
- Certain kernel-mode components and some CPU intrinsics have different behavior or availability under Arm64EC and must be reviewed.
- Packaging and installer configurations must be updated so the correct binaries are deployed to ARM64 devices.
