# ComfyUI — sample repository source

**Upstream:** [`comfyanonymous/ComfyUI`](https://github.com/comfyanonymous/ComfyUI)

**Pinned commit SHA:** `387f98aa2822f684b8597959a52a467d88cc4806`

**Branch at time of assessment:** `master`

**License:** GPL-3.0. Repository content is not included in this fixture bundle;
only the produced `assessment.json`, `plan.json`, `score.json`, and (once
generated) `patches/`, `migration-result.json`, `validation.json`, `report.md`,
and `report.html` are committed.

## Reproducing the assessment

The Feature 1 CLI resolves the branch tip to an immutable commit SHA and
downloads that commit's ZIP archive via the GitHub REST API — no `git clone`
required. The output SHA is captured on `assessment.json.repository.commitSha`.

```powershell
dotnet run --project backend/Assessment/RepositoryDiscovery/RepositoryDiscovery.csproj `
  --no-build `
  -- `
  https://github.com/comfyanonymous/ComfyUI `
  --output samples/comfyui/assessment.json
```

Because `dotnet run --project` treats the project directory as the current
working directory, adjust the `--output` path if invoked from a different shell.

## Why ComfyUI

Contrast with Open WebUI — ComfyUI is a Python-first application with a
straightforward dependency graph and no native MSBuild projects. Feature 1
should classify it as a moderate-effort port rather than a native-binary
migration.
