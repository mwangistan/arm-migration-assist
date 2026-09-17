"""
Reusable DEV_SPEC-style deck builder.

Renders a 16:9 PowerPoint deck (960x540 pt) from a plain Python dict.
Content lives in a separate content module so text can iterate freely without
touching layout code.

Supported slide types:
    - title            Cover slide with orange accent bar.
    - bullets          Header + bullet list.
    - kv_rows          Header + list of (key, value, accent) rows.
    - columns          Header + N side-by-side header/body cards.
    - two_col          Header + two panels with title + body.
    - workflow         Header + horizontal step chips (arrows between chips) +
                       description strip.
    - system_diagram   Header + labelled node boxes at explicit (x, y) positions
                       with arrow connectors between named nodes.

Each non-title slide gets a navy header (title + subtitle) and a light footer
bar with deck footer text on the left and page number on the right.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path
from typing import Iterable, Mapping, Sequence

from lxml import etree
from pptx import Presentation
from pptx.dml.color import RGBColor
from pptx.enum.shapes import MSO_CONNECTOR_TYPE, MSO_SHAPE
from pptx.enum.text import MSO_ANCHOR
from pptx.oxml.ns import qn
from pptx.util import Pt


# --- Theme -----------------------------------------------------------------

NAVY = RGBColor(0x10, 0x2A, 0x43)
TEAL = RGBColor(0x00, 0x78, 0x8C)
ORANGE = RGBColor(0xE6, 0x7E, 0x22)
PANEL = RGBColor(0xF4, 0xF6, 0xF9)
INK = RGBColor(0x1A, 0x1A, 0x1A)
SUBTLE = RGBColor(0xCF, 0xDA, 0xE6)
MUTED = RGBColor(0x4A, 0x55, 0x68)
WHITE = RGBColor(0xFF, 0xFF, 0xFF)

ACCENTS: Mapping[str, RGBColor] = {
    "navy": NAVY,
    "teal": TEAL,
    "orange": ORANGE,
    "panel": PANEL,
    "ink": INK,
    "muted": MUTED,
    "white": WHITE,
}

FONT_NAME = "Segoe UI"
SLIDE_W_PT = 960
SLIDE_H_PT = 540
CONTENT_LEFT = 43
CONTENT_RIGHT = 917  # 960 - 43
CONTENT_TOP = 83
CONTENT_BOTTOM = 510


@dataclass
class DeckSpec:
    title: str
    footer: str
    slides: Sequence[Mapping]
    theme_font: str = FONT_NAME


# --- Low-level primitives --------------------------------------------------


def _pt(v: float) -> "Pt":
    return Pt(v)


def _add_rect(slide, left, top, width, height, fill: RGBColor):
    shape = slide.shapes.add_shape(
        MSO_SHAPE.RECTANGLE, _pt(left), _pt(top), _pt(width), _pt(height)
    )
    shape.fill.solid()
    shape.fill.fore_color.rgb = fill
    shape.line.fill.background()
    shape.shadow.inherit = False
    return shape


def _add_text(
    slide,
    text: str,
    left,
    top,
    width,
    height,
    *,
    size: int,
    color: RGBColor,
    bold: bool = False,
    font_name: str = FONT_NAME,
    anchor: str = "top",
    space_after: int = 0,
):
    box = slide.shapes.add_textbox(_pt(left), _pt(top), _pt(width), _pt(height))
    tf = box.text_frame
    tf.margin_left = _pt(0)
    tf.margin_right = _pt(0)
    tf.margin_top = _pt(0)
    tf.margin_bottom = _pt(0)
    tf.word_wrap = True
    if anchor == "middle":
        tf.vertical_anchor = MSO_ANCHOR.MIDDLE
    elif anchor == "bottom":
        tf.vertical_anchor = MSO_ANCHOR.BOTTOM
    else:
        tf.vertical_anchor = MSO_ANCHOR.TOP

    lines = text.split("\n") if text else [""]
    for idx, line in enumerate(lines):
        para = tf.paragraphs[0] if idx == 0 else tf.add_paragraph()
        para.text = ""
        para.space_after = _pt(space_after)
        run = para.add_run()
        run.text = line
        run.font.name = font_name
        run.font.size = _pt(size)
        run.font.color.rgb = color
        run.font.bold = bold
    return box


def _resolve_accent(name: str | None, default: RGBColor = TEAL) -> RGBColor:
    if not name:
        return default
    return ACCENTS.get(name, default)


def _add_arrow(
    slide,
    x1: float,
    y1: float,
    x2: float,
    y2: float,
    *,
    color: RGBColor = MUTED,
    width: float = 1.25,
) -> None:
    connector = slide.shapes.add_connector(
        MSO_CONNECTOR_TYPE.STRAIGHT, _pt(x1), _pt(y1), _pt(x2), _pt(y2)
    )
    connector.line.color.rgb = color
    connector.line.width = _pt(width)
    ln = connector.line._get_or_add_ln()
    for existing in ln.findall(qn("a:tailEnd")):
        ln.remove(existing)
    tail_end = etree.SubElement(ln, qn("a:tailEnd"))
    tail_end.set("type", "triangle")
    tail_end.set("w", "med")
    tail_end.set("h", "med")


# --- Slide chrome ----------------------------------------------------------


def _add_header(slide, title: str, subtitle: str) -> None:
    _add_rect(slide, 0, 0, SLIDE_W_PT, 65, NAVY)
    _add_text(slide, title, 29, 9, 902, 36, size=24, color=WHITE, bold=False)
    if subtitle:
        _add_text(slide, subtitle, 29, 40, 902, 25, size=12, color=SUBTLE)


def _add_footer(slide, footer_text: str, page_number: int) -> None:
    _add_rect(slide, 0, 515, SLIDE_W_PT, 25, PANEL)
    _add_text(slide, footer_text, 29, 517, 576, 22, size=9, color=MUTED)
    _add_text(
        slide,
        str(page_number),
        902,
        517,
        36,
        22,
        size=9,
        color=MUTED,
        anchor="top",
    )


# --- Slide renderers -------------------------------------------------------


def _render_title(slide, spec: Mapping) -> None:
    _add_rect(slide, 0, 0, SLIDE_W_PT, SLIDE_H_PT, NAVY)
    _add_rect(slide, 0, 187, SLIDE_W_PT, 4, ORANGE)
    _add_text(
        slide,
        spec["title"],
        43,
        86,
        874,
        94,
        size=44,
        color=WHITE,
        bold=False,
    )
    if spec.get("subtitle"):
        _add_text(
            slide, spec["subtitle"], 43, 144, 874, 50, size=20, color=SUBTLE
        )
    if spec.get("tagline"):
        _add_text(
            slide, spec["tagline"], 43, 220, 874, 40, size=14, color=SUBTLE
        )
    if spec.get("footnote"):
        _add_text(
            slide, spec["footnote"], 43, 480, 874, 40, size=11, color=SUBTLE
        )


def _render_bullets(slide, spec: Mapping) -> None:
    top = 90
    if spec.get("intro"):
        _add_text(
            slide, spec["intro"], 43, top, 874, 40, size=14, color=NAVY, bold=True
        )
        top += 46
    items: Sequence[str] = spec.get("items", ())
    body = "\n".join(f"\u2022  {item}" for item in items)
    _add_text(
        slide,
        body,
        43,
        top,
        874,
        CONTENT_BOTTOM - top,
        size=spec.get("font_size", 14),
        color=INK,
        space_after=spec.get("space_after", 6),
    )
    if spec.get("callout"):
        _add_rect(slide, 43, 452, 874, 46, PANEL)
        _add_text(
            slide,
            spec["callout"],
            58,
            460,
            844,
            32,
            size=12,
            color=NAVY,
            bold=True,
        )


def _render_kv_rows(slide, spec: Mapping) -> None:
    rows: Sequence[Mapping | Sequence] = spec.get("rows", ())
    row_h = spec.get("row_height", 43)
    gap = spec.get("row_gap", 8)
    top = spec.get("start_top", 86)
    key_w = spec.get("key_width", 187)
    value_w = SLIDE_W_PT - 43 - key_w - 43
    if spec.get("intro"):
        _add_text(slide, spec["intro"], 43, top - 2, 874, 30, size=13, color=NAVY, bold=True)
        top += 30
    for row in rows:
        if isinstance(row, Mapping):
            key = row.get("key", "")
            value = row.get("value", "")
            accent = _resolve_accent(row.get("accent"), TEAL)
        else:
            key, value, *rest = row
            accent = _resolve_accent(rest[0] if rest else None, TEAL)
        _add_rect(slide, 43, top, key_w, row_h, accent)
        _add_text(
            slide,
            key,
            50,
            top,
            key_w - 14,
            row_h,
            size=11,
            color=WHITE,
            bold=True,
            anchor="middle",
        )
        _add_rect(slide, 43 + key_w, top, value_w, row_h, PANEL)
        _add_text(
            slide,
            value,
            43 + key_w + 11,
            top,
            value_w - 22,
            row_h,
            size=11,
            color=INK,
            anchor="middle",
        )
        top += row_h + gap
    if spec.get("footnote"):
        _add_text(
            slide,
            spec["footnote"],
            43,
            CONTENT_BOTTOM - 20,
            874,
            25,
            size=10,
            color=MUTED,
        )


def _render_columns(slide, spec: Mapping) -> None:
    cols: Sequence[Mapping | Sequence] = spec.get("columns", ())
    if not cols:
        return
    gap = 15
    total_w = 874
    col_w = (total_w - gap * (len(cols) - 1)) / len(cols)
    top = spec.get("start_top", 90)
    head_h = spec.get("head_height", 43)
    body_h = spec.get("body_height", 310)
    if spec.get("intro"):
        _add_text(slide, spec["intro"], 43, top, 874, 30, size=14, color=NAVY, bold=True)
        top += 34
    for idx, col in enumerate(cols):
        if isinstance(col, Mapping):
            head = col.get("head", "")
            body = col.get("body", "")
            accent_name = col.get("accent")
        else:
            head, body, *rest = col
            accent_name = rest[0] if rest else None
        default_cycle = (TEAL, ORANGE, NAVY)
        accent = _resolve_accent(accent_name, default_cycle[idx % 3])
        left = 43 + idx * (col_w + gap)
        _add_rect(slide, left, top, col_w, head_h, accent)
        _add_text(
            slide,
            head,
            left + 12,
            top,
            col_w - 24,
            head_h,
            size=14,
            color=WHITE,
            bold=True,
            anchor="middle",
        )
        _add_rect(slide, left, top + head_h + 2, col_w, body_h, PANEL)
        _add_text(
            slide,
            body,
            left + 15,
            top + head_h + 10,
            col_w - 30,
            body_h - 20,
            size=11,
            color=INK,
            space_after=4,
        )
    if spec.get("callout"):
        _add_text(
            slide,
            spec["callout"],
            43,
            top + head_h + body_h + 18,
            874,
            30,
            size=12,
            color=NAVY,
            bold=True,
        )


def _render_two_col(slide, spec: Mapping) -> None:
    top = spec.get("start_top", 90)
    if spec.get("intro"):
        _add_text(slide, spec["intro"], 43, top, 874, 30, size=14, color=NAVY, bold=True)
        top += 34
    panel_w = 432
    gap = 10
    left_pane = spec.get("left", {})
    right_pane = spec.get("right", {})
    for idx, pane in enumerate((left_pane, right_pane)):
        left = 43 + idx * (panel_w + gap)
        accent = _resolve_accent(pane.get("accent"), TEAL if idx == 0 else ORANGE)
        _add_text(
            slide,
            pane.get("title", ""),
            left,
            top,
            panel_w,
            29,
            size=14,
            color=accent,
            bold=True,
        )
        panel_h = spec.get("panel_height", 360)
        _add_rect(slide, left, top + 32, panel_w, panel_h, PANEL)
        body = pane.get("body", "")
        if isinstance(body, (list, tuple)):
            body = "\n".join(f"\u2022  {item}" for item in body)
        _add_text(
            slide,
            body,
            left + 15,
            top + 39,
            panel_w - 30,
            panel_h - 14,
            size=11,
            color=INK,
            space_after=4,
        )


def _render_workflow(slide, spec: Mapping) -> None:
    steps: Sequence[Mapping] = spec.get("steps", ())
    if not steps:
        return
    top = spec.get("start_top", 90)
    gap = 10
    total_w = 874
    chip_w = (total_w - gap * (len(steps) - 1)) / len(steps)
    chip_h = 60
    for idx, step in enumerate(steps):
        left = 43 + idx * (chip_w + gap)
        accent = _resolve_accent(step.get("accent"), TEAL if idx % 2 == 0 else ORANGE)
        _add_rect(slide, left, top, chip_w, chip_h, accent)
        _add_text(
            slide,
            step.get("number", f"{idx + 1}"),
            left + 8,
            top + 4,
            chip_w - 16,
            22,
            size=11,
            color=WHITE,
            bold=True,
        )
        _add_text(
            slide,
            step.get("label", ""),
            left + 8,
            top + 26,
            chip_w - 16,
            32,
            size=14,
            color=WHITE,
            bold=True,
            anchor="middle",
        )
        if idx < len(steps) - 1:
            arrow_y = top + chip_h / 2
            _add_arrow(
                slide,
                left + chip_w + 0.5,
                arrow_y,
                left + chip_w + gap - 0.5,
                arrow_y,
                color=MUTED,
                width=1.5,
            )
    desc_top = top + chip_h + 18
    desc_h = 44
    for idx, step in enumerate(steps):
        left = 43 + idx * (chip_w + gap)
        _add_rect(slide, left, desc_top, chip_w, desc_h, PANEL)
        _add_text(
            slide,
            step.get("description", ""),
            left + 8,
            desc_top + 6,
            chip_w - 16,
            desc_h - 12,
            size=10,
            color=INK,
        )
    if spec.get("callout"):
        _add_rect(slide, 43, desc_top + desc_h + 20, 874, 46, PANEL)
        _add_text(
            slide,
            spec["callout"],
            58,
            desc_top + desc_h + 28,
            844,
            32,
            size=13,
            color=NAVY,
            bold=True,
        )
    if spec.get("bullets"):
        body = "\n".join(f"\u2022  {item}" for item in spec["bullets"])
        _add_text(
            slide,
            body,
            43,
            desc_top + desc_h + 78,
            874,
            120,
            size=12,
            color=INK,
            space_after=4,
        )


def _render_system_diagram(slide, spec: Mapping) -> None:
    nodes_by_id: dict[str, tuple[float, float, float, float]] = {}
    if spec.get("intro"):
        _add_text(slide, spec["intro"], 43, 78, 874, 24, size=13, color=NAVY, bold=True)
    for node in spec.get("nodes", ()):
        x = float(node["x"])
        y = float(node["y"])
        w = float(node.get("w", 160))
        h = float(node.get("h", 50))
        accent = _resolve_accent(node.get("accent"), TEAL)
        _add_rect(slide, x, y, w, h, PANEL)
        _add_rect(slide, x, y, w, 5, accent)
        label_top = y + 9
        label_h = h - (18 if node.get("sublabel") else 12)
        _add_text(
            slide,
            node.get("label", ""),
            x + 8,
            label_top,
            w - 16,
            label_h,
            size=node.get("size", 12),
            color=INK,
            bold=True,
            anchor="middle",
        )
        if node.get("sublabel"):
            _add_text(
                slide,
                node["sublabel"],
                x + 8,
                y + h - 18,
                w - 16,
                14,
                size=9,
                color=MUTED,
            )
        nodes_by_id[node["id"]] = (x, y, w, h)

    for edge in spec.get("edges", ()):
        if edge["from"] not in nodes_by_id or edge["to"] not in nodes_by_id:
            continue
        sx, sy, sw, sh = nodes_by_id[edge["from"]]
        tx, ty, tw, th = nodes_by_id[edge["to"]]
        scx, scy = sx + sw / 2, sy + sh / 2
        tcx, tcy = tx + tw / 2, ty + th / 2
        if abs(tcx - scx) > abs(tcy - scy):
            if tcx > scx:
                x1, y1, x2, y2 = sx + sw, scy, tx, tcy
            else:
                x1, y1, x2, y2 = sx, scy, tx + tw, tcy
        else:
            if tcy > scy:
                x1, y1, x2, y2 = scx, sy + sh, tcx, ty
            else:
                x1, y1, x2, y2 = scx, sy, tcx, ty + th
        _add_arrow(slide, x1, y1, x2, y2, color=MUTED, width=1.25)
        if edge.get("label"):
            mx, my = (x1 + x2) / 2, (y1 + y2) / 2
            _add_text(
                slide,
                edge["label"],
                mx - 55,
                my - 8,
                110,
                14,
                size=9,
                color=NAVY,
                bold=True,
            )

    if spec.get("callout"):
        _add_rect(slide, 43, 462, 874, 40, PANEL)
        _add_text(
            slide,
            spec["callout"],
            58,
            469,
            844,
            26,
            size=12,
            color=NAVY,
            bold=True,
        )


_RENDERERS = {
    "title": _render_title,
    "bullets": _render_bullets,
    "kv_rows": _render_kv_rows,
    "columns": _render_columns,
    "two_col": _render_two_col,
    "workflow": _render_workflow,
    "system_diagram": _render_system_diagram,
}


# --- Entry point -----------------------------------------------------------


def build_deck(spec: Mapping | DeckSpec, output_path: str | Path) -> Path:
    if isinstance(spec, Mapping):
        deck = DeckSpec(
            title=spec.get("title", ""),
            footer=spec.get("footer", ""),
            slides=spec.get("slides", ()),
            theme_font=spec.get("theme_font", FONT_NAME),
        )
    else:
        deck = spec

    prs = Presentation()
    prs.slide_width = _pt(SLIDE_W_PT)
    prs.slide_height = _pt(SLIDE_H_PT)
    blank_layout = prs.slide_layouts[6]

    for idx, slide_spec in enumerate(deck.slides, start=1):
        slide = prs.slides.add_slide(blank_layout)
        stype = slide_spec.get("type", "bullets")
        renderer = _RENDERERS.get(stype)
        if renderer is None:
            raise ValueError(f"Unknown slide type: {stype!r}")
        if stype != "title":
            _add_header(
                slide,
                slide_spec.get("title", ""),
                slide_spec.get("subtitle", ""),
            )
            _add_footer(slide, deck.footer, idx)
        renderer(slide, slide_spec)

    out = Path(output_path)
    out.parent.mkdir(parents=True, exist_ok=True)
    prs.save(str(out))
    return out


if __name__ == "__main__":  # pragma: no cover - smoke test
    demo = {
        "title": "Deck builder smoke test",
        "footer": "Smoke test",
        "slides": [
            {"type": "title", "title": "Hello", "subtitle": "It works"},
            {
                "type": "bullets",
                "title": "Overview",
                "subtitle": "smoke",
                "items": ["one", "two", "three"],
            },
        ],
    }
    build_deck(demo, Path(__file__).with_name("_smoke.pptx"))
