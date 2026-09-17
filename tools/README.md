# Dependency scanning tool: Microsoft component-detection

Feature 1 enriches its dependency matrix with
[Microsoft component-detection](https://github.com/microsoft/component-detection),
an SBOM scanner that resolves **pip, npm, NuGet, Cargo, Go, Maven, RubyGems, CocoaPods,
Vcpkg** (and more), including full transitive dependency graphs.

The executable is ~80 MB and is **not committed** (see `.gitignore`). It is an *optional*
enrichment: if it is absent, `DependencyScanSkill` falls back to the built-in manifest
parsers and assessments still run.

## Install (Windows x64)

Download the self-contained binary into this folder as `component-detection.exe`:

```powershell
$tag = 'v7.1.13'   # or the latest release
Invoke-WebRequest `
  -Uri "https://github.com/microsoft/component-detection/releases/download/$tag/component-detection-win-x64.exe" `
  -OutFile "$PSScriptRoot\component-detection.exe" -UseBasicParsing
```

Other platforms: pick the matching asset (`component-detection-linux-x64`,
`component-detection-osx-arm64`, ...) and name it `component-detection` (no extension).

## How it is located at runtime

`ComponentDetectionScanner.ResolveExecutable()` searches, in order:

1. the `COMPONENT_DETECTION_PATH` environment variable (full path to the executable),
2. a `tools/` folder next to the app or up the directory tree (this folder),
3. the executable name on `PATH`.

## Requirements

- The **PipReport** detector shells out to `pip`, so Python + pip must be installed and
  online for Python dependency resolution (other ecosystems have no such requirement).
- Single-file extraction is redirected to a writable `.cd-extract/` folder under the
  analysis workspace via `DOTNET_BUNDLE_EXTRACT_BASE_DIR`.
