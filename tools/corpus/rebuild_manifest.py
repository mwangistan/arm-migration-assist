"""Rebuild ``knowledge/windows-on-arm/corpus.json`` from the snippet files.

Reads a metadata catalog (this file, ``METADATA`` dict), walks ``snippets/``,
computes SHA-256 and byte length for each file, and writes the manifest.

Run whenever snippet content or metadata changes:

    python tools/corpus/rebuild_manifest.py

The manifest is authoritative for metadata (guidanceId, title, section, topics,
sourceUrl, retrievedAt, summary). Snippet files contain only markdown body.

Cross-record rules (unique guidanceId and relativePath) are validated by the
sibling ``validate_corpus.py`` script.
"""

from __future__ import annotations

import hashlib
import json
from datetime import datetime, timezone
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
CORPUS_ROOT = REPO_ROOT / "knowledge" / "windows-on-arm"
SNIPPETS_DIR = CORPUS_ROOT / "snippets"
MANIFEST_PATH = CORPUS_ROOT / "corpus.json"


CORPUS_VERSION = "2026-09-17.1"
SCHEMA_VERSION = "1.0"
PRODUCER = {
    "name": "arm-migration-assist-corpus",
    "version": "0.1.0",
    "sourceKind": "hand-authored-prototype",
    "toolCommit": None,
}
NOTES = (
    "Bootstrap corpus with three hand-authored snippets. Replace with a "
    "sanctioned Microsoft Learn snapshot before shipping the demo."
)


# One entry per snippet file. relativePath, sha256, and byteLength are filled
# in from disk. Everything else is authored here and kept under review.
METADATA = [
    {
        "guidanceId": "woa-overview-01",
        "sourceUrl": "https://learn.microsoft.com/en-us/windows/arm/overview",
        "title": "Windows on Arm overview",
        "section": "Overview",
        "retrievedAt": "2026-09-14T00:00:00Z",
        "relativePath": "snippets/woa-overview-01.md",
        "topics": ["woa-overview"],
        "summary": "Windows 11 supports ARM64 natively and emulates x64; native ARM64 unlocks best performance and battery.",
    },
    {
        "guidanceId": "arm64ec-overview-01",
        "sourceUrl": "https://learn.microsoft.com/en-us/windows/arm/arm64ec",
        "title": "Arm64EC overview",
        "section": "Overview",
        "retrievedAt": "2026-09-14T00:00:00Z",
        "relativePath": "snippets/arm64ec-overview-01.md",
        "topics": ["arm64ec-overview", "arm64ec-abi"],
        "summary": "Arm64EC lets one process mix native ARM64 code with x64 modules under emulation, enabling incremental migration.",
    },
    {
        "guidanceId": "add-arm-support-01",
        "sourceUrl": "https://learn.microsoft.com/en-us/windows/arm/add-arm-support",
        "title": "Add Arm support to your Windows app",
        "section": "Assess, plan, build, validate",
        "retrievedAt": "2026-09-14T00:00:00Z",
        "relativePath": "snippets/add-arm-support-01.md",
        "topics": [
            "add-arm-support",
            "native-arm64-build",
            "compatibility-troubleshooting",
        ],
        "summary": "Four-stage workflow (assess, plan, build, validate) for adding ARM64 support to an existing Windows app.",
    },
    {
        "guidanceId": "python-woa-wheels-01",
        "sourceUrl": "https://learn.arm.com/learning-paths/laptops-and-desktops/win_python/how-to-1/",
        "title": "Platform-specificity of Python packages on Windows on Arm",
        "section": "Native wheels, source builds, and repo-level patterns",
        "retrievedAt": "2026-09-17T00:00:00Z",
        "relativePath": "snippets/python-woa-wheels-01.md",
        "topics": [
            "python-packaging",
            "compatibility-troubleshooting",
        ],
        "summary": "Python packages with compiled extensions need win_arm64 wheels; without them pip falls back to source builds requiring MSVC and often Rust.",
    },
    {
        "guidanceId": "pytorch-woa-status-01",
        "sourceUrl": "https://pytorch.org/",
        "title": "PyTorch on Windows on Arm \u2014 install status and GPU-parity gap",
        "section": "CPU wheels, CUDA gap, DirectML, ONNX fallback",
        "retrievedAt": "2026-09-17T00:00:00Z",
        "relativePath": "snippets/pytorch-woa-status-01.md",
        "topics": [
            "python-packaging",
            "ai-inference-arm64",
            "compatibility-troubleshooting",
        ],
        "summary": "PyTorch ships CPU-only win_arm64 wheels; CUDA is unavailable on Windows on Arm, so GPU parity requires torch-directml or ONNX Runtime.",
    },
    {
        "guidanceId": "github-actions-windows-arm-runner-01",
        "sourceUrl": "https://docs.github.com/en/actions",
        "title": "GitHub Actions windows-11-arm runner and setup-python on ARM64",
        "section": "CI matrix patterns and common failure modes",
        "retrievedAt": "2026-09-17T00:00:00Z",
        "relativePath": "snippets/github-actions-windows-arm-runner-01.md",
        "topics": [
            "windows-arm-ci-runners",
            "native-arm64-build",
        ],
        "summary": "Use runs-on: windows-11-arm with actions/setup-python architecture: arm64; watch for source-build fallbacks and cache-key architecture collisions.",
    },
    {
        "guidanceId": "onnxruntime-arm64-01",
        "sourceUrl": "https://onnxruntime.ai/",
        "title": "ONNX Runtime on Windows on Arm \u2014 the primary native-ARM64 inference path",
        "section": "Export, serve, and warnings",
        "retrievedAt": "2026-09-17T00:00:00Z",
        "relativePath": "snippets/onnxruntime-arm64-01.md",
        "topics": [
            "ai-inference-arm64",
            "native-arm64-build",
        ],
        "summary": "ONNX Runtime is the default native-ARM64 inference fallback for CUDA-dependent workloads on Windows on Arm; QNN provider targets Snapdragon X NPUs.",
    },
]


def _sha256_and_len(path: Path) -> tuple[str, int]:
    data = path.read_bytes()
    return hashlib.sha256(data).hexdigest(), len(data)


def main() -> int:
    snippets = []
    for entry in METADATA:
        rel = entry["relativePath"]
        path = CORPUS_ROOT / rel
        if not path.exists():
            print(f"MISSING: {rel}")
            return 1
        digest, byte_len = _sha256_and_len(path)
        snippets.append(
            {
                **entry,
                "sha256": digest,
                "byteLength": byte_len,
            }
        )

    manifest = {
        "schemaVersion": SCHEMA_VERSION,
        "corpusVersion": CORPUS_VERSION,
        "generatedAt": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "producer": PRODUCER,
        "source": "microsoft-learn-windows-on-arm",
        "snippets": snippets,
        "notes": NOTES,
    }

    MANIFEST_PATH.write_text(
        json.dumps(manifest, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )
    print(f"Wrote {MANIFEST_PATH.relative_to(REPO_ROOT)} ({len(snippets)} snippets)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
