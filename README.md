# ARM Migration Assist

AI-powered engineering assistant for accelerating Windows on Arm application readiness and migration.

A developer provides a repository URL and gets back an evidence-based readiness assessment, a prioritized migration plan, proposed code and configuration changes, and validation results.

## Tech stack (from the spec)

| Area | Choice |
|------|--------|
| Frontend | React + Fluent UI |
| Backend | .NET API |
| AI | Azure OpenAI or approved internal endpoint (optional Phi) |
| Code analysis | Tree-sitter, Roslyn, Clang, project-file parsers |
| Repository access | GitHub URL + local clone |
| Output | HTML dashboard, JSON manifest, Markdown/DOCX report, patch/PR bundle |

## Project structure

The folders map directly to the four features in the spec, so anyone reading the
spec can find where their code goes. Each feature folder holds the user stories
under it.

```text
arm-migration-assist/
├── frontend/                  # React + Fluent UI dashboard
│
├── backend/                   # .NET API
│   ├── Assessment/            # Feature 1: Repository Analysis
│   │   ├── RepositoryDiscovery/     # Story 1.1 / 1.2
│   │   ├── DependencyScanner/       # Story 1.3
│   │   └── CodeCompatibility/       # Story 1.4
│   │
│   ├── MigrationPlanner/      # Feature 2: AI Migration Planner
│   │   ├── ReadinessScoring/        # Story 2.1
│   │   ├── StrategyGenerator/       # Story 2.2
│   │   └── ReportGeneration/        # Story 2.3
│   │
│   ├── AutomatedMigration/    # Feature 3: Automated Migration Actions
│   │   ├── BuildConfiguration/      # Story 3.1
│   │   ├── PipelineUpdates/         # Story 3.2
│   │   └── CodeMigration/           # Story 3.3
│   │
│   └── Validation/            # Feature 4: Validation and Demo
│       ├── BuildValidation/         # Story 4.1
│       └── Dashboard/               # Story 4.2 (backend support)
│
└── samples/                   # Reference repos for the demo (Story 4.3)
    ├── comfyui/
    └── open-webui/
```

## How the folders map to the spec

| Spec | Folder |
|------|--------|
| Feature 1: Repository Analysis | `backend/Assessment/` |
| Feature 2: AI Migration Planner | `backend/MigrationPlanner/` |
| Feature 3: Automated Migration Actions | `backend/AutomatedMigration/` |
| Feature 4: Validation & Demo | `backend/Validation/` + `samples/` |
| Frontend dashboard | `frontend/` |
| Reference repos (ComfyUI, Open WebUI) | `samples/` |

Each subfolder name matches a user story. AI, GitHub, and code-analysis code lives
inside whichever feature uses it — there are no shared/infra folders, to keep things
simple.

## Workflow

The product follows the six stages in the spec:

| Stage | What happens | Folder |
|-------|--------------|--------|
| 1. Connect | Provide a GitHub URL or local path; pick target (ARM64 native or Arm64EC) | `frontend/` |
| 2. Assess | Discover languages, dependencies, build system, and architecture-specific code | `backend/Assessment/` |
| 3. Plan | Score readiness and recommend a migration strategy | `backend/MigrationPlanner/` |
| 4. Transform | Generate reviewable build, pipeline, and code changes | `backend/AutomatedMigration/` |
| 5. Validate | Build, run checks/tests, capture pass/fail evidence | `backend/Validation/` |
| 6. Package | Export the report, diffs, and decision log | `backend/MigrationPlanner/ReportGeneration/` |

Keep the boundaries simple: assessment produces facts with file evidence, the planner
scores and recommends, automated migration produces reviewable patches, and validation
records measured build/test outcomes.
