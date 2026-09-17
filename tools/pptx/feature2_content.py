"""Slide content for the Feature 2 (AI Migration Planner) DEV_SPEC deck.

Edit ``SLIDES`` (or the top-level metadata) to change the deck. Re-run
``build_feature2.py`` to regenerate the PowerPoint file. No layout code lives
here; the reusable renderer lives in ``deck_builder.py``.
"""

from __future__ import annotations


DECK_TITLE = "ARM Migration Assist"
DECK_FOOTER = "ARM Migration Assist \u2014 Feature 2 DEV_SPEC review deck"
OUTPUT_FILENAME = "ARM_Migration_Assist_Feature_2_Overview.pptx"


# Illustrative numbers appear on the scoring dimension slides. The scorer's
# real weights, deductions, and caps live in the scoring decision record and
# can differ from what is shown here.
CALIBRATION_NOTE = (
    "Illustrative formulas \u00b7 final numbers live in the scoring decision "
    "record and may differ."
)


SLIDES = [
    {
        "type": "title",
        "title": "AI Migration Planner",
        "subtitle": "Evidence-based Windows on Arm migration planning",
        "tagline": "Feature 2 DEV_SPEC v1 \u00b7 planning-only iteration",
        "footnote": "Prepared for the ARM Migration Assist hackathon review",
    },
    {
        "type": "bullets",
        "title": "What Feature 2 delivers",
        "subtitle": "DEV_SPEC \u00a71 \u00b7 planning layer between Assessment and Automated Migration",
        "intro": "Turns an evidence-based repository assessment into a validated, approval-gated migration plan.",
        "items": [
            "Recommends one of: native-arm64, arm64ec, staged, winui3, or insufficient-evidence.",
            "Preserves a deterministic readiness score exactly; the AI agent reasons over it, never mutates it.",
            "Every finding, work item, risk, and acceptance test is linked to evidence IDs.",
            "Every implementation action maps to an available reusable skill or a proposed missing skill.",
            "Planning-only: no repository writes, no builds, no shell, no commits, no publishing, no PRs.",
        ],
        "callout": "Reusable framework value first \u00b7 one recommendation is a demo; the planner is the product.",
    },
    {
        "type": "workflow",
        "title": "Happy-path workflow",
        "subtitle": "DEV_SPEC \u00a73 \u00b7 the planner owns Score \u2192 Plan; Feature 1 and 3 own the ends",
        "steps": [
            {"number": "01", "label": "Assess", "description": "Feature 1 emits RepositoryAssessmentV1 with scanner evidence.", "accent": "teal"},
            {"number": "02", "label": "Score", "description": "Deterministic scorer produces ReadinessScoreV1 + caps.", "accent": "orange"},
            {"number": "03", "label": "Plan", "description": "AI planner returns MigrationPlanV1 with evidence links.", "accent": "teal"},
            {"number": "04", "label": "Approve", "description": "Human reviews, approves, and gates any write skill.", "accent": "orange"},
            {"number": "05", "label": "Transform", "description": "Feature 3 generates ARM64/Arm64EC changes on approval.", "accent": "teal"},
            {"number": "06", "label": "Validate", "description": "Feature 4 runs builds, tests, and WoA hardware checks.", "accent": "orange"},
        ],
        "callout": "The planner is one node in the flow; contracts stay stable so features 1, 3, and 4 evolve independently.",
    },
    {
        "type": "system_diagram",
        "title": "System architecture \u2014 the moving parts",
        "subtitle": "DEV_SPEC \u00a74 \u00b7 layered .NET 8 solution behind IPlannerModel and IMcpTool",
        "nodes": [
            {"id": "feature1", "x": 43, "y": 90, "w": 200, "h": 48, "label": "Feature 1 output", "sublabel": "RepositoryAssessmentV1", "accent": "orange"},
            {"id": "client", "x": 727, "y": 90, "w": 190, "h": 48, "label": "Client / UI", "sublabel": "Feature 4 dashboard", "accent": "orange"},
            {"id": "api", "x": 315, "y": 90, "w": 330, "h": 48, "label": "POST /api/migration-plans", "sublabel": "Minimal API \u00b7 JSON Schema validated", "accent": "navy"},
            {"id": "orch", "x": 43, "y": 170, "w": 874, "h": 55, "label": "Migration Planner Orchestrator", "sublabel": "validate \u2192 score \u2192 tools \u2192 model \u2192 verify \u2192 audit", "accent": "teal"},
            {"id": "evval", "x": 43, "y": 258, "w": 160, "h": 55, "label": "Evidence Validator", "sublabel": "IDs \u00b7 references \u00b7 uniqueness", "accent": "teal"},
            {"id": "scorer", "x": 220, "y": 258, "w": 160, "h": 55, "label": "Deterministic Scorer", "sublabel": "5 dims \u00b7 caps \u00b7 confidence", "accent": "teal"},
            {"id": "mcp", "x": 397, "y": 258, "w": 160, "h": 55, "label": "MCP Tool Registry", "sublabel": "9 read-only tools", "accent": "teal"},
            {"id": "model", "x": 574, "y": 258, "w": 160, "h": 55, "label": "IPlannerModel", "sublabel": "structured JSON out", "accent": "teal"},
            {"id": "corpus", "x": 751, "y": 258, "w": 166, "h": 55, "label": "Corpus Loader", "sublabel": "SHA-256 verified", "accent": "teal"},
            {"id": "mockmcp", "x": 397, "y": 348, "w": 160, "h": 42, "label": "Mock MCP Server", "accent": "orange"},
            {"id": "fakemodel", "x": 574, "y": 348, "w": 160, "h": 42, "label": "Fake / Hosted Model", "accent": "orange"},
            {"id": "corpusjson", "x": 751, "y": 348, "w": 166, "h": 42, "label": "corpus.json + snippets", "sublabel": "Blob later, same contract", "accent": "orange"},
            {"id": "audit", "x": 43, "y": 410, "w": 874, "h": 45, "label": "Audit Logger + Plan Safety Validator", "sublabel": "runId \u00b7 evidence IDs \u00b7 guidanceIds \u00b7 corpusVersion \u2014 no secrets, no source", "accent": "navy"},
        ],
        "edges": [
            {"from": "feature1", "to": "api"},
            {"from": "client", "to": "api"},
            {"from": "api", "to": "orch"},
            {"from": "orch", "to": "scorer"},
            {"from": "orch", "to": "mcp"},
            {"from": "orch", "to": "model"},
            {"from": "mcp", "to": "mockmcp"},
            {"from": "model", "to": "fakemodel"},
            {"from": "corpus", "to": "corpusjson"},
            {"from": "orch", "to": "audit"},
        ],
        "callout": "Every backend is behind an interface. Fake/mock ship in v1; hosted model and blob-backed corpus swap in without touching contracts.",
    },
    {
        "type": "workflow",
        "title": "Runtime data flow \u2014 one plan request",
        "subtitle": "DEV_SPEC \u00a75 \u00b7 Validate \u2192 Score \u2192 Reason \u2192 Verify \u2192 Audit",
        "steps": [
            {"number": "1", "label": "Ingest", "description": "Schema-validate RepositoryAssessmentV1. Reject unsupported version.", "accent": "teal"},
            {"number": "2", "label": "Ground", "description": "Load availableSkills[] + WoA corpus version. Snapshot allowlist.", "accent": "orange"},
            {"number": "3", "label": "Tools", "description": "Invoke read-only MCP tools. Record every call in audit.", "accent": "teal"},
            {"number": "4", "label": "Score", "description": "Deterministic scorer emits ReadinessScoreV1 (never model-writable).", "accent": "orange"},
            {"number": "5", "label": "Reason", "description": "Call IPlannerModel with structured output request.", "accent": "teal"},
            {"number": "6", "label": "Verify", "description": "Plan validator: schema, evidence, skills, cycles, safety, score identity.", "accent": "orange"},
            {"number": "7", "label": "Return", "description": "Emit MigrationPlanV1 + warnings. Audit closes the run.", "accent": "teal"},
        ],
        "callout": "The model reasons; the deterministic layers gate. Any tampered score, hallucinated evidence, or unsafe instruction fails the pipeline.",
    },
    {
        "type": "kv_rows",
        "title": "Input contract \u2014 RepositoryAssessmentV1",
        "subtitle": "DEV_SPEC \u00a76 \u00b7 versioned, evidence-based, produced by Feature 1",
        "rows": [
            ("schemaVersion", "Versioned envelope; unsupported versions rejected with Problem Details.", "navy"),
            ("repository", "name \u00b7 url \u00b7 commitSha \u00b7 defaultBranch \u00b7 license", "teal"),
            ("technology", "languages \u00b7 frameworks \u00b7 projectTypes \u00b7 buildSystems \u00b7 packageManagers \u00b7 installers \u00b7 ciSystems", "teal"),
            ("dependencies[]", "ecosystem \u00b7 type (native/COM/plugin/driver) \u00b7 architectureStatus \u00b7 replacements \u00b7 evidence[] \u00b7 confidence", "teal"),
            ("codeFindings[]", "ruleId \u00b7 category \u00b7 severity \u00b7 file/line \u00b7 evidence \u00b7 confidence", "teal"),
            ("buildFindings", "arm64TargetExists \u00b7 arm64EcTargetExists \u00b7 arm64CiJobExists \u00b7 packaging \u00b7 tests \u00b7 evidence[]", "teal"),
            ("windowsExperience", "UI tech \u00b7 installer \u00b7 offlineCapable \u00b7 accessibility \u00b7 notifications \u00b7 lifecycle", "teal"),
            ("scanCoverage", "filesScanned/filesTotal \u00b7 dependencyResolutionRate \u00b7 scannersCompleted/scannersFailed", "orange"),
            ("availableSkills[]", "Reusable agent/skill catalog: name \u00b7 version \u00b7 writeAccess \u00b7 supportedInputs/outputs", "orange"),
        ],
        "row_height": 34,
        "row_gap": 4,
        "start_top": 78,
        "key_width": 200,
        "footnote": "Every evidence entry has a unique ID with source type, path/artifact, and observation. Duplicate or malformed IDs are rejected.",
    },
    {
        "type": "kv_rows",
        "title": "Deterministic readiness scoring \u2014 the five dimensions",
        "subtitle": "DEV_SPEC \u00a77 \u00b7 configurable rules \u00b7 reproducible \u00b7 confidence calculated separately",
        "rows": [
            ("Dependency compat 30%", "ARM64 status, native/COM/driver classification, replacements, criticality \u00d7 confidence.", "teal"),
            ("Code compat 25%", "SIMD, inline asm, pointer-size, P/Invoke, conditional compilation, concurrency risks.", "teal"),
            ("Build & CI 20%", "ARM64/Arm64EC targets, packaging, CI job coverage, tests present, artifact publication.", "teal"),
            ("Runtime & validation 15%", "Test coverage evidence, prior ARM run signals, static-check coverage.", "orange"),
            ("Windows experience 10%", "UI technology fit, installer, offline mode, accessibility, notifications, lifecycle.", "orange"),
        ],
        "row_height": 42,
        "row_gap": 6,
        "start_top": 90,
        "key_width": 220,
        "footnote": "Returns per-dimension raw \u00b7 deduction \u00b7 contribution \u00b7 evidence IDs \u00b7 uncapped + final \u00b7 rationale codes \u00b7 confidence \u00b7 provisional flag.",
    },
    {
        "type": "system_diagram",
        "title": "Scoring pipeline \u2014 how a score is built",
        "subtitle": "DEV_SPEC \u00a77.1 \u00b7 signals \u2192 weighted sum \u2192 caps \u2192 band \u00b7 confidence in parallel",
        "nodes": [
            {"id": "dim1", "x": 43, "y": 100, "w": 155, "h": 48, "label": "Dependency 30%", "sublabel": "raw + deductions", "accent": "teal"},
            {"id": "dim2", "x": 208, "y": 100, "w": 155, "h": 48, "label": "Code 25%", "sublabel": "raw + deductions", "accent": "teal"},
            {"id": "dim3", "x": 373, "y": 100, "w": 155, "h": 48, "label": "Build & CI 20%", "sublabel": "raw + deductions", "accent": "teal"},
            {"id": "dim4", "x": 538, "y": 100, "w": 155, "h": 48, "label": "Runtime 15%", "sublabel": "raw + deductions", "accent": "orange"},
            {"id": "dim5", "x": 703, "y": 100, "w": 155, "h": 48, "label": "Windows 10%", "sublabel": "raw + deductions", "accent": "orange"},
            {"id": "sum", "x": 300, "y": 190, "w": 300, "h": 50, "label": "Weighted sum \u2192 uncapped score", "sublabel": "\u03a3 (weight\u1d62 \u00d7 raw\u1d62)  \u2208 [0, 100]", "accent": "navy"},
            {"id": "prov", "x": 50, "y": 275, "w": 210, "h": 90, "label": "Provisional?", "sublabel": "coverage < 60% or scanner failed \u2192 true", "accent": "teal"},
            {"id": "caps", "x": 300, "y": 275, "w": 300, "h": 50, "label": "Apply blocker caps", "sublabel": "driver \u2264 30 \u00b7 x64 native \u2264 40 \u00b7 no ARM target \u2264 60", "accent": "orange"},
            {"id": "conf", "x": 700, "y": 275, "w": 210, "h": 90, "label": "Confidence", "sublabel": "weighted mean of dim confidences \u2192 high/medium/low", "accent": "teal"},
            {"id": "final", "x": 300, "y": 360, "w": 300, "h": 55, "label": "Final score + band", "sublabel": "ready \u00b7 moderate \u00b7 significant \u00b7 blocked", "accent": "navy"},
        ],
        "edges": [
            {"from": "dim1", "to": "sum"},
            {"from": "dim2", "to": "sum"},
            {"from": "dim3", "to": "sum"},
            {"from": "dim4", "to": "sum"},
            {"from": "dim5", "to": "sum"},
            {"from": "sum", "to": "caps"},
            {"from": "caps", "to": "final"},
            {"from": "sum", "to": "conf"},
            {"from": "sum", "to": "prov"},
        ],
        "callout": "The model may explain the score. The model may not recompute or override it. Score identity is verified during plan validation.",
    },
    {
        "type": "kv_rows",
        "title": "Dimension 1 \u2014 Dependency compatibility (30%)",
        "subtitle": "DEV_SPEC \u00a77.2 \u00b7 how ARM64 availability of dependencies becomes a score",
        "intro": "How ARM64-ready the dependencies are \u2014 heaviest weight because one blocked required dep can block the whole migration.",
        "rows": [
            ("Signals in", "dependencies[].type \u00b7 criticality \u00b7 architectureStatus \u00b7 availableArchitectures \u00b7 replacementCandidates \u00b7 confidence", "navy"),
            ("Raw score (illustrative)", "100 \u00d7 \u03a3(w\u1d62 \u00d7 status\u1d62) / \u03a3(w\u1d62)  \u00b7  w\u1d62 = 3 required / 1 optional  \u00b7  status\u1d62 = ready 1.0 / emulation 0.5 / unknown 0.3 / blocked 0", "teal"),
            ("Deductions", "\u221240 per required blocked without replacement \u00b7 \u221220 per required emulation-only \u00b7 \u221215 per required unknown", "orange"),
            ("Cap linkage", "Triggers 'required x64-only native \u2264 40' and 'required unsupported driver \u2264 30' caps.", "orange"),
            ("Confidence contribution", "Weighted mean of dependencies[].confidence \u00d7 scanCoverage.dependencyResolutionRate.", "teal"),
            ("Rationale codes", "DEP-BLOCKED-{name} \u00b7 DEP-EMULATION-{name} \u00b7 DEP-UNKNOWN-{name} \u00b7 DEP-REPLACEMENT-AVAILABLE-{name}", "teal"),
        ],
        "row_height": 46,
        "row_gap": 6,
        "start_top": 90,
        "key_width": 220,
        "footnote": CALIBRATION_NOTE,
    },
    {
        "type": "kv_rows",
        "title": "Dimension 2 \u2014 Code compatibility (25%)",
        "subtitle": "DEV_SPEC \u00a77.3 \u00b7 how architecture-specific code patterns pull the score down",
        "intro": "Whether source has ARM64-hostile patterns \u2014 inline asm, SSE/AVX intrinsics, x64 conditionals, x64-only P/Invoke.",
        "rows": [
            ("Signals in", "codeFindings[].severity \u00b7 category \u00b7 confidence \u00b7 file/line \u00b7 scanCoverage.filesScanned/filesTotal", "navy"),
            ("Raw score (illustrative)", "100 \u2212 \u03a3(severity_weight\u1d62 \u00d7 confidence\u1d62), floored at 0.", "teal"),
            ("Severity weights", "critical 25 \u00b7 high 12 \u00b7 medium 5 \u00b7 low 1 \u00b7 informational 0", "orange"),
            ("Cap linkage", "No direct cap. Concentrated critical findings still push the score below the 'blocked' band.", "orange"),
            ("Confidence contribution", "Scales with filesScanned/filesTotal. Coverage below 60% forces provisional.", "teal"),
            ("Rationale codes", "CODE-CRIT-{ruleId} \u00b7 CODE-HIGH-{ruleId} \u00b7 CODE-COVERAGE-LOW", "teal"),
        ],
        "row_height": 46,
        "row_gap": 6,
        "start_top": 90,
        "key_width": 220,
        "footnote": CALIBRATION_NOTE,
    },
    {
        "type": "kv_rows",
        "title": "Dimension 3 \u2014 Build & CI readiness (20%)",
        "subtitle": "DEV_SPEC \u00a77.4 \u00b7 what already builds and ships for ARM64",
        "intro": "Whether the repo already produces ARM64 artifacts \u2014 no ARM64 or Arm64EC target caps the final score at 60.",
        "rows": [
            ("Signals in", "buildFindings.arm64TargetExists \u00b7 arm64EcTargetExists \u00b7 arm64CiJobExists \u00b7 packagingSupportsArm64 \u00b7 testsExist \u00b7 detectedTargets[]", "navy"),
            ("Raw score (illustrative)", "40 \u00b7 arm64Target + 20 \u00b7 arm64EcTarget + 20 \u00b7 arm64CiJob + 10 \u00b7 packaging + 10 \u00b7 tests  (booleans as 0/1)", "teal"),
            ("Cap linkage", "No ARM64 or Arm64EC target \u2192 final score capped at 60 regardless of other dimensions.", "orange"),
            ("Confidence contribution", "Proportional to buildFindings.evidence[] count; 0 evidence \u2192 low confidence.", "teal"),
            ("Rationale codes", "BUILD-NO-ARM64 \u00b7 BUILD-NO-CI \u00b7 BUILD-NO-PACKAGING \u00b7 BUILD-NO-TESTS", "teal"),
        ],
        "row_height": 52,
        "row_gap": 6,
        "start_top": 90,
        "key_width": 220,
        "footnote": CALIBRATION_NOTE,
    },
    {
        "type": "kv_rows",
        "title": "Dimension 4 \u2014 Runtime and validation evidence (15%)",
        "subtitle": "DEV_SPEC \u00a77.5 \u00b7 what verifiable behavior exists",
        "intro": "Verifiable behavioral evidence the migration can be validated \u2014 low coverage flips the result to provisional.",
        "rows": [
            ("Signals in", "buildFindings.testsExist \u00b7 scanCoverage.scannersCompleted \u00b7 scannersFailed \u00b7 dependencyResolutionRate \u00b7 prior ARM signals", "navy"),
            ("Raw score (illustrative)", "60 \u00b7 testsExist + 30 \u00b7 dependencyResolutionRate + 10 \u00b7 (scannersCompleted / (completed + failed))", "teal"),
            ("Deductions", "\u221240 if testsExist = false \u00b7 \u221220 if dependencyResolutionRate < 0.5 \u00b7 \u221215 per non-empty scannersFailed[]", "orange"),
            ("Cap linkage", "No direct cap. Contributes to provisional flag via coverage.", "orange"),
            ("Confidence contribution", "Direct: coverage completeness is the dominant signal.", "teal"),
            ("Rationale codes", "RUN-NO-TESTS \u00b7 RUN-COVERAGE-LOW \u00b7 RUN-SCANNER-FAILED-{name}", "teal"),
        ],
        "row_height": 46,
        "row_gap": 6,
        "start_top": 90,
        "key_width": 220,
        "footnote": CALIBRATION_NOTE,
    },
    {
        "type": "kv_rows",
        "title": "Dimension 5 \u2014 Windows experience and deployment (10%)",
        "subtitle": "DEV_SPEC \u00a77.6 \u00b7 how Windows-native the target already is",
        "intro": "How Windows-native the app already is \u2014 weakness nudges the plan toward a WinUI 3 modernization path.",
        "rows": [
            ("Signals in", "windowsExperience.uiTechnology \u00b7 offlineCapable \u00b7 installerExists \u00b7 accessibilityEvidence \u00b7 notificationsIntegrated \u00b7 lifecycleIntegrated", "navy"),
            ("Raw score (illustrative)", "25 \u00b7 uiFit + 20 \u00b7 offlineCapable + 15 \u00b7 installerExists + 15 \u00b7 accessibility + 15 \u00b7 notifications + 10 \u00b7 lifecycle", "teal"),
            ("uiFit table", "winui3 / wpf / winforms / uwp / maui = 1.0 \u00b7 qt / electron / tauri = 0.6 \u00b7 web = 0.4 \u00b7 cli = 0.2 \u00b7 none / unknown = 0", "orange"),
            ("Cap linkage", "No direct cap. Weak Windows experience nudges recommendation toward WinUI 3 as an alternative path.", "orange"),
            ("Confidence contribution", "Proportional to evidence[] entries for this section.", "teal"),
            ("Rationale codes", "WIN-UI-{tech} \u00b7 WIN-NO-INSTALLER \u00b7 WIN-NO-OFFLINE \u00b7 WIN-ACCESSIBILITY-{level}", "teal"),
        ],
        "row_height": 46,
        "row_gap": 6,
        "start_top": 90,
        "key_width": 220,
        "footnote": CALIBRATION_NOTE,
    },
    {
        "type": "two_col",
        "title": "Blocker caps and readiness bands",
        "subtitle": "DEV_SPEC \u00a77.7 \u00b7 caps applied after uncapped score \u00b7 unknowns never become positive",
        "left": {
            "title": "Blocker caps",
            "accent": "orange",
            "body": [
                "Required unsupported driver \u2192 maximum 30.",
                "Required x64-only native dependency without replacement \u2192 maximum 40.",
                "No ARM64 or Arm64EC build target detected \u2192 maximum 60.",
                "Insufficient scan coverage \u2192 result marked provisional; readiness cannot be inferred.",
            ],
        },
        "right": {
            "title": "Bands",
            "accent": "teal",
            "body": [
                "85\u2013100 \u00b7 ready-or-minor-changes.",
                "70\u201384 \u00b7 moderate-migration.",
                "50\u201369 \u00b7 significant-remediation.",
                "0\u201349 \u00b7 blocked-or-major-redesign.",
                "Confidence: high \u00b7 medium \u00b7 low, computed independently of readiness.",
            ],
        },
        "panel_height": 300,
    },
    {
        "type": "two_col",
        "title": "Confidence and provisional state",
        "subtitle": "DEV_SPEC \u00a77.8 \u00b7 readiness and confidence are separate axes",
        "left": {
            "title": "Confidence calculation",
            "accent": "teal",
            "body": [
                "Per-dimension confidence \u2208 [0, 1] rolled up as a weighted mean.",
                "Bands: high \u2265 0.75 \u00b7 medium 0.4\u20130.75 \u00b7 low < 0.4.",
                "Independent of readiness \u2014 a repo can score 85 with medium confidence.",
                "The AI planner cites confidence in facts/inferences but cannot recompute it.",
            ],
        },
        "right": {
            "title": "Provisional triggers",
            "accent": "orange",
            "body": [
                "scanCoverage.filesScanned / filesTotal < 0.6.",
                "scannersFailed[] is non-empty.",
                "dependencyResolutionRate < 0.75.",
                "Any required dimension has 0 evidence entries.",
                "Provisional = true \u2192 band label suffixed 'provisional' \u00b7 recommendation may be 'insufficient-evidence'.",
            ],
        },
        "panel_height": 300,
    },
    {
        "type": "kv_rows",
        "title": "Output contract \u2014 MigrationPlanV1",
        "subtitle": "DEV_SPEC \u00a78 \u00b7 no additional properties allowed",
        "rows": [
            ("recommendedPath", "native-arm64 \u00b7 arm64ec \u00b7 staged \u00b7 winui3 \u00b7 insufficient-evidence.", "navy"),
            ("confidence", "high \u00b7 medium \u00b7 low; independent of readiness score.", "teal"),
            ("facts / inferences", "Statements with evidenceIds[] + guidanceIds[]; inferences carry confidence.", "teal"),
            ("alternatives[]", "path \u00b7 disposition (rejected/deferred/viable) \u00b7 rationale \u00b7 evidenceIds[].", "teal"),
            ("workItems[]", "P0/P1/P2 \u00b7 objective \u00b7 agentOrSkill \u00b7 inputs/outputs \u00b7 dependencies \u00b7 acceptanceTests \u00b7 approvalRequired.", "teal"),
            ("missingSkills[]", "proposedName \u00b7 purpose \u00b7 required inputs/outputs \u00b7 justification \u00b7 evidenceIds[].", "teal"),
            ("validationPlan", "target devices \u00b7 build/functional/reliability/perf/power/offline/accessibility/WinExp checks.", "orange"),
            ("risks / unknowns", "severity \u00b7 mitigation \u00b7 evidenceIds[] \u00b7 requiredSkill for unknowns.", "orange"),
            ("reusableOutputs", "Artifacts the next repository can inherit \u2014 the framework value.", "orange"),
        ],
        "row_height": 34,
        "row_gap": 4,
        "start_top": 78,
        "key_width": 200,
        "footnote": "Every work item requires approval. Manual coding, execution, mutation, publishing, or PR-creation instructions are rejected.",
    },
    {
        "type": "columns",
        "title": "Read-only MCP tools \u2014 the planner's only I/O",
        "subtitle": "DEV_SPEC \u00a79 \u00b7 explicit allowlist \u00b7 typed schemas \u00b7 evidence IDs on every return",
        "intro": "Nine tools. No writes. No shell. No arbitrary URLs. Unknown tool requests are rejected.",
        "columns": [
            {
                "head": "Assessment surface",
                "accent": "teal",
                "body": (
                    "\u2022  get_repository_summary\n"
                    "\u2022  get_dependency_findings\n"
                    "\u2022  get_code_compatibility_findings\n"
                    "\u2022  get_build_readiness_findings\n"
                    "\u2022  get_windows_experience_findings"
                ),
            },
            {
                "head": "Coverage and skills",
                "accent": "orange",
                "body": (
                    "\u2022  get_scan_coverage\n"
                    "\u2022  get_available_skill_catalog\n\n"
                    "Coverage gaps surface as provisional. Skill gaps become entries in missingSkills[], never silent assumptions."
                ),
            },
            {
                "head": "Scoring and guidance",
                "accent": "navy",
                "body": (
                    "\u2022  calculate_readiness  (deterministic)\n"
                    "\u2022  lookup_windows_arm_guidance  (curated corpus)\n\n"
                    "The planner may cite the score and guidance but cannot alter either."
                ),
            },
        ],
        "start_top": 88,
        "body_height": 280,
        "callout": "Repository content, dependency metadata, and tool output are treated as untrusted evidence, not instructions.",
    },
    {
        "type": "bullets",
        "title": "Windows on Arm guidance as grounded evidence",
        "subtitle": "DEV_SPEC \u00a79.9 \u00b7 lookup_windows_arm_guidance serves a curated corpus, not the internet",
        "intro": "Microsoft Learn Windows on Arm guidance is bundled locally, versioned, and cited by stable guidanceId.",
        "items": [
            "Corpus covers: Windows on Arm overview, Add Arm support, Arm64EC overview and ABI, Update Arm32 to Arm64, WinUI 3 and Windows App SDK, MSIX packaging for ARM64.",
            "Each snippet ships with guidanceId \u00b7 sourceUrl \u00b7 title \u00b7 section \u00b7 retrievedAt \u00b7 corpusVersion \u00b7 sha256.",
            "Planner facts, inferences, alternatives, work items, risks, and validation checks may cite guidance IDs alongside assessment evidence IDs.",
            "Validation resolves every guidanceId. Unknown IDs are treated as hallucinations and fail the plan.",
            "Arbitrary URLs, free-text web queries, and live fetches are rejected. Only bundled snippets are returnable.",
            "Refreshing the corpus is a maintainer-only, offline job. The planner never triggers a refresh.",
        ],
        "callout": "Grounded guidance closes a prompt-injection surface and makes plans reproducible across runs.",
    },
    {
        "type": "bullets",
        "title": "Safety boundaries \u2014 planning-only, always",
        "subtitle": "DEV_SPEC \u00a710 \u00b7 what the planner refuses to do, by contract",
        "items": [
            "No repository writes, builds, shell commands, commits, publishing, or pull requests in this iteration.",
            "Repository content, dependency metadata, and tool output are untrusted evidence, never instructions.",
            "Every work item requires human approval before any future write-capable skill runs.",
            "No manual-coding instructions to humans; the workflow generates changes through agents/skills.",
            "Deterministic score is authoritative; the model may explain it but cannot alter it.",
            "Audit trail redacts credentials, secrets, and raw source content by default.",
        ],
        "callout": "Feature 3 receives an approval-gated plan. It never executes an unapproved action.",
    },
    {
        "type": "bullets",
        "title": "Plan validation pipeline",
        "subtitle": "DEV_SPEC \u00a711 \u00b7 runs after model response, before returning success",
        "items": [
            "Parse JSON \u2192 validate against MigrationPlanV1 schema; reject additional properties.",
            "Verify assessmentId matches; verify schemaVersion is supported.",
            "Resolve every evidenceId and guidanceId; unknown references fail the plan.",
            "Verify every workItem.agentOrSkill exists in availableSkills[] or is declared in missingSkills[].",
            "Verify workItem dependencies reference valid IDs and the graph is acyclic.",
            "Verify every workItem sets approvalRequired = true.",
            "Reject manual-coding, execution, mutation, publishing, or PR-creation instructions.",
            "Verify the deterministic score field is byte-identical to the scorer output.",
        ],
        "callout": "Failures surface as Problem Details with stable error codes and safe diagnostics.",
    },
    {
        "type": "kv_rows",
        "title": "Fixtures \u2014 end-to-end assertions",
        "subtitle": "DEV_SPEC \u00a712 \u00b7 four fixtures cover the full recommendation space",
        "rows": [
            ("ready-managed-app", "Managed code \u00b7 ARM64-ready deps \u00b7 build+CI present \u2192 expected native-arm64.", "teal"),
            ("moderate-build-gap", "Compatible code+deps \u00b7 missing ARM64 build, CI, packaging \u2192 native-arm64 with build-focused work.", "teal"),
            ("blocked-native-app", "Required x64-only native dep \u00b7 SIMD/asm \u00b7 no ARM64 target \u2192 arm64ec or staged; must trigger a cap.", "orange"),
            ("incomplete-assessment", "Failed scanners \u00b7 low coverage \u00b7 unresolved deps \u2192 insufficient-evidence; provisional confidence.", "orange"),
        ],
        "row_height": 60,
        "row_gap": 8,
        "start_top": 100,
        "key_width": 210,
        "footnote": "Tests assert paths, caps, provisional flags, and safety validation \u2014 not exact model wording. Fake model keeps CI credential-free.",
    },
    {
        "type": "bullets",
        "title": "Implementation phases",
        "subtitle": "DEV_SPEC \u00a713 \u00b7 vertical slices; offline planner works before hosted model exists",
        "items": [
            "1. Contracts and JSON Schemas: assessment, score, plan, evidence, skills.",
            "2. Evidence, skill, safety, and dependency validators.",
            "3. Configurable deterministic scorer with blocker caps and confidence.",
            "4. Typed read-only MCP abstraction, allowlist, mock server, and Windows on Arm guidance corpus.",
            "5. Fake model, planner orchestration, minimal API, audit logging, and Problem Details.",
            "6. Four fixtures, integration tests, hosted-model adapter, documentation, and prompt log.",
        ],
        "callout": "Definition of done: builds and tests pass without credentials; every fixture returns a validated plan; recommendations are evidence-linked, approval-gated, and safe.",
    },
    {
        "type": "two_col",
        "title": "Rollout status \u00b7 what we need from reviewers",
        "subtitle": "DEV_SPEC \u00a714 \u00b7 planning surface only; write-capable skills come after review",
        "left": {
            "title": "Where we are",
            "accent": "orange",
            "body": [
                "Feature 2 module scaffold in place under backend/MigrationPlanner/.",
                "RepositoryAssessmentV1 and WindowsOnArmGuidanceCorpusV1 schemas hardened and validated.",
                "Bootstrap WoA corpus with SHA-256 verification and negative tamper test.",
                "MCP allowlist, safety validation, and scoring design captured in the plan.",
                "Fake-model pipeline planned so the deck is renderable offline.",
            ],
        },
        "right": {
            "title": "What we need",
            "accent": "teal",
            "body": [
                "Sign-off on the RepositoryAssessmentV1 field set with Feature 1 owners.",
                "Sign-off on the MigrationPlanV1 field set with Feature 3 and 4 owners.",
                "Confirm scoring weights, deductions, and blocker caps for the first iteration.",
                "Approve the initial Windows on Arm guidance corpus contents and refresh policy.",
                "Approve the approved-model provider before enabling the hosted path.",
            ],
        },
        "panel_height": 300,
    },
]


DECK_SPEC = {
    "title": DECK_TITLE,
    "footer": DECK_FOOTER,
    "slides": SLIDES,
}
