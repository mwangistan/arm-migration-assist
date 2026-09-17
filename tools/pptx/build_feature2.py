"""Render the Feature 2 DEV_SPEC deck to a .pptx file.

Usage:
    python tools/pptx/build_feature2.py                # writes to repo root
    python tools/pptx/build_feature2.py -o path.pptx   # writes to custom path

The renderer lives in ``deck_builder.py``. Edit content in
``feature2_content.py`` and re-run this script; layout and theme stay stable.
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

SCRIPT_DIR = Path(__file__).resolve().parent
REPO_ROOT = SCRIPT_DIR.parent.parent
sys.path.insert(0, str(SCRIPT_DIR))

from deck_builder import build_deck  # noqa: E402
from feature2_content import DECK_SPEC, OUTPUT_FILENAME  # noqa: E402


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "-o",
        "--output",
        type=Path,
        default=REPO_ROOT / OUTPUT_FILENAME,
        help="Output .pptx path (default: repo root).",
    )
    args = parser.parse_args()

    written = build_deck(DECK_SPEC, args.output)
    print(f"Wrote {written} ({written.stat().st_size} bytes, {len(DECK_SPEC['slides'])} slides)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
