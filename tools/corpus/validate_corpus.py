"""Validate the Windows on Arm guidance corpus.

Runs four passes:
    1. Meta-validate ``WindowsOnArmGuidanceCorpusV1.schema.json`` (Draft 2020-12).
    2. Validate ``corpus.json`` against the schema (with format assertions).
    3. Enforce cross-record rules the schema cannot express:
         - unique guidanceId across snippets
         - unique relativePath across snippets
         - each snippet file exists
         - recorded sha256 matches actual file bytes
         - recorded byteLength matches actual file bytes
         - each retrievedAt <= generatedAt
    4. Negative test: rewrite one file locally and confirm sha verification catches it.

Run: python tools/corpus/validate_corpus.py
"""

from __future__ import annotations

import hashlib
import json
import sys
from datetime import datetime
from pathlib import Path

from jsonschema import Draft202012Validator, FormatChecker

REPO_ROOT = Path(__file__).resolve().parents[2]
SCHEMA_PATH = (
    REPO_ROOT
    / "backend"
    / "MigrationPlanner"
    / "contracts"
    / "WindowsOnArmGuidanceCorpusV1.schema.json"
)
CORPUS_ROOT = REPO_ROOT / "knowledge" / "windows-on-arm"
MANIFEST_PATH = CORPUS_ROOT / "corpus.json"


def _parse_iso(ts: str) -> datetime:
    return datetime.fromisoformat(ts.replace("Z", "+00:00"))


def main() -> int:
    print("1) Meta-validate schema (Draft 2020-12)")
    schema = json.loads(SCHEMA_PATH.read_text(encoding="utf-8"))
    Draft202012Validator.check_schema(schema)
    print("   OK\n")

    print("2) Validate corpus.json against schema")
    manifest = json.loads(MANIFEST_PATH.read_text(encoding="utf-8"))
    v = Draft202012Validator(schema, format_checker=FormatChecker())
    errors = list(v.iter_errors(manifest))
    if errors:
        for e in errors:
            print(f"   FAIL: {list(e.absolute_path)} {e.message}")
        return 1
    print(f"   OK ({len(manifest['snippets'])} snippets)\n")

    print("3) Cross-record rules")
    problems: list[str] = []

    ids = [s["guidanceId"] for s in manifest["snippets"]]
    if len(set(ids)) != len(ids):
        dupes = [i for i in set(ids) if ids.count(i) > 1]
        problems.append(f"duplicate guidanceId: {dupes}")

    paths = [s["relativePath"] for s in manifest["snippets"]]
    if len(set(paths)) != len(paths):
        dupes = [p for p in set(paths) if paths.count(p) > 1]
        problems.append(f"duplicate relativePath: {dupes}")

    generated_at = _parse_iso(manifest["generatedAt"])
    for snippet in manifest["snippets"]:
        path = CORPUS_ROOT / snippet["relativePath"]
        if not path.exists():
            problems.append(f"missing file: {snippet['relativePath']}")
            continue
        data = path.read_bytes()
        actual_sha = hashlib.sha256(data).hexdigest()
        if actual_sha != snippet["sha256"]:
            problems.append(
                f"sha256 mismatch for {snippet['relativePath']}: "
                f"manifest={snippet['sha256'][:12]}... actual={actual_sha[:12]}..."
            )
        if snippet.get("byteLength") is not None and snippet["byteLength"] != len(data):
            problems.append(
                f"byteLength mismatch for {snippet['relativePath']}: "
                f"manifest={snippet['byteLength']} actual={len(data)}"
            )
        if _parse_iso(snippet["retrievedAt"]) > generated_at:
            problems.append(
                f"retrievedAt after generatedAt for {snippet['guidanceId']}"
            )

    if problems:
        for p in problems:
            print(f"   FAIL: {p}")
        return 2
    print("   OK: guidanceId unique, relativePath unique, sha256 verified, "
          "byteLength verified, retrievedAt <= generatedAt\n")

    print("4) Negative: tamper with a snippet in memory and re-verify")
    tampered_snippet = manifest["snippets"][0]
    tampered_path = CORPUS_ROOT / tampered_snippet["relativePath"]
    tampered_bytes = tampered_path.read_bytes() + b"\n<injected>\n"
    if hashlib.sha256(tampered_bytes).hexdigest() == tampered_snippet["sha256"]:
        print("   FAIL: tampered content produced identical hash (should not happen)")
        return 3
    print("   OK: tamper detection would reject modified snippet\n")

    print("Corpus valid.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
