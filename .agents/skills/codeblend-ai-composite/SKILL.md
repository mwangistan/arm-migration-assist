---
name: codeblend-ai-composite
description: Run or explain AI-readiness evaluation with the bundled CLI, or use its deterministic substrate and evidence-pack stages for local diagnostics without model/API access. Use for composite scores, structural readiness, full or two-axis assessment, and questions about rubric, weights, tiers, consensus and scoring. Do not require assessment before ordinary local coding.
---

# Combined AI-Readiness

Use this skill when assessment or methodology is the task, not as a prerequisite
for an ordinary code, test, setup or documentation change. Those tasks can use
the local transformation workflow without a score.

For full assessment, run the bundled evaluator for the host operating system once. The executable
owns repository inspection, evidence collection, Copilot judge consensus,
scoring, conservative execution-aware documentation drift analysis, report
generation, and CSV export; do not reproduce those stages manually.

For questions about how the evaluation is designed or how scores are
calculated, read and cite [`how-it-work.md`](./how-it-work.md). Do not run an
evaluation merely to explain the methodology.

## Choose the requested result

| Need | Supported path |
|---|---|
| Explain the methodology | Read `how-it-work.md`; no evaluation |
| Inspect local structural readiness | `substrate <local-path> --output <new-file>` |
| Collect a local evidence pack | `evidence-pack <local-path> --output <new-file>` |
| Compute the full composite score | `eval <repo>` with its actual runtime/evidence prerequisites |
| Fix code or improve local workflow | Use the transformation skills; an assessment is optional |

The deterministic `substrate` and `evidence-pack` stages do not collect provider
API evidence or invoke Copilot judges. Use them for useful local diagnostics
when full evaluation is unavailable or unnecessary. For example:

```powershell
& $Evaluator substrate $RepoRoot --output $NewOutputFile
```

Select the native executable as described below and preserve existing outputs.
Label these results as structural evidence or an evidence pack, **not a full
composite/ARI score**. Do not fabricate missing judge or API results.

## Full evaluation workflow

1. Determine the target directory:
   - Use the directory supplied by the user when one is provided.
   - Otherwise use the user's current working directory.
   - Resolve it to the repository root with
     `git -C <target> rev-parse --show-toplevel`.
   - Stop with the Git error if the target is not inside a Git repository.

2. Detect the host operating system and architecture, then resolve the matching
   executable beside this `SKILL.md`:
   - On Windows amd64, use `ai-readiness-eval.exe`.
   - On Linux amd64 (including Ubuntu x86_64), use
     `ai-readiness-eval-linux-amd64`.
   - On macOS amd64, use `ai-readiness-eval-darwin-amd64`.
   - On macOS arm64 (Apple silicon), use
     `ai-readiness-eval-darwin-arm64`.
   - In an installed plugin, resolve the selected filename under
     `${CLAUDE_PLUGIN_ROOT}/skills/codeblend-ai-composite/`.
   - In the source lab, resolve it in this skill directory.
   - On Linux and macOS, run `chmod +x` on the selected executable before invoking it in
     case the plugin installation did not preserve executable permissions.
   - Stop this assessment step clearly on Linux ARM64 or another unsupported platform; do
     not try to run the Windows executable through Wine.
   - Stop this assessment step clearly if the executable is missing; do not fall back to the legacy
     Python composite pipeline or scanner.

   On Windows, require
   `[Runtime.InteropServices.RuntimeInformation]::OSArchitecture` to be `X64`.
   On Linux, require `uname -m` to return `x86_64` or `amd64`. On macOS, require
   `uname -m` to return `x86_64`, `amd64`, `arm64`, or `aarch64`. Use the actual
   host result rather than inferring the platform from repository paths or the
   user's shell syntax.

3. Run the evaluation against the resolved repository root:

   Windows PowerShell:

   ```powershell
   & $Evaluator eval $RepoRoot
   ```

   Linux or macOS shell:

   ```bash
   "$evaluator" eval "$repo_root"
   ```

   Keep the user's current working directory unchanged. Pass through any
   evaluation flags the user explicitly supplied, such as `--models`,
   `--max-rounds`, `--allow-missing-api-evidence`, `--full-findings`, or
   `--no-cache`.

4. Read the run directory printed by the CLI, then present its `composite.md`.
   Lead with the headline score, AI-ready verdict, substrate level, operation
   tier, highest-priority remediation items, and documentation drift coverage
   and residual action. Treat semantic review as advisory and automatic repair
   as a backstop; only a deterministic repository-wide blocking PR gate is
   complete coverage. Surface the continuous-cleanup automation rating when
   reported; the evaluator refines generic scheduled cleanup guidance only when
   executable cleanup capability is independently
   proven. Also report the run directory so the user can access the JSON and
   CSV artifacts.

   Coverage and maturity labels describe the evaluator's evidence model; they
   are not instructions to install new gates or block unrelated local work.

## Runtime requirements and unavailable evidence

- `git` and GitHub Copilot CLI must be available on `PATH`.
- GitHub repositories require an authenticated `gh` session for API evidence.
- Azure DevOps repositories require an authenticated `az` session.
- Do not add `--allow-missing-api-evidence` unless the user explicitly accepts
  a degraded evaluation.

These requirements govern the full assessment, not ordinary local development.
If optional API/model access is unavailable, offer the supported deterministic
stages or continue the requested local improvement and report the limitation.
A requested full/qualified score must still meet its real evidence contract.

## Feedback

Report skill issues at
https://github.com/gim-home/codeblend-skills-lab/issues.
