# Windows on Arm overview

Windows on Arm refers to editions of Windows that run natively on ARM64 processors. Windows 11 supports ARM64 as a first-class architecture and includes an emulator that transparently runs x86 and x64 applications on ARM64 hardware.

Native ARM64 applications avoid emulation overhead and can take full advantage of ARM64 features, including improved power efficiency on devices such as Snapdragon X-series and future Windows on Arm PCs.

Applications that ship only x64 binaries will still run under emulation, but at a performance and power cost. Applications with native ARM64 builds run without emulation.

## Key facts

- Windows 11 ARM64 supports native ARM64, x86, and x64 applications.
- Native ARM64 applications typically deliver better performance and battery life than the same application running under x64 emulation.
- Applications with native code dependencies (drivers, kernel components, or x64-only native libraries) may block or complicate ARM64 support.
- Some architecture-specific code (for example, x86 SSE intrinsics, inline assembly, or pointer-size assumptions) will not port cleanly and must be reviewed.

## When ARM64 support matters most

Applications that emphasize battery life, sustained performance, cold-start time, or a native Windows experience benefit most from a native ARM64 build. Long-running background services, media pipelines, and interactive productivity apps are typical wins.
