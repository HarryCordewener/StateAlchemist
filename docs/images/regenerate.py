#!/usr/bin/env python3
"""Rebuild the published logo files from the editable master, StateAlchemist300dpi.svg.

The master sets its text in the generic families Sans and Serif, which every viewer resolves to its own fonts. So
the published SVGs have their text converted to outlines, using the fonts on the machine that runs this (the logo
was designed with Noto Sans Bold and Noto Serif Bold), and render the same everywhere.

Writes, next to this script:
  StateAlchemist.svg        outlines, for light backgrounds
  StateAlchemist-dark.svg   outlines, black swapped for GitHub's dark-theme foreground (#e6edf3)
  StateAlchemist.png        1024 px wide, transparent, for light backgrounds
  StateAlchemist-dark.png   1024 px wide, transparent, for dark backgrounds

Needs Inkscape 1.x on the PATH. Run: python3 docs/images/regenerate.py
"""
import pathlib
import re
import subprocess

HERE = pathlib.Path(__file__).resolve().parent
MASTER = HERE / "StateAlchemist300dpi.svg"
LIGHT = HERE / "StateAlchemist.svg"
DARK = HERE / "StateAlchemist-dark.svg"
INK, DARK_INK = "#000000", "#e6edf3"


def outline() -> str:
    subprocess.run(["inkscape", str(MASTER), "--export-text-to-path", "--export-plain-svg", "--export-type=svg",
                    "-o", str(LIGHT)], check=True, capture_output=True)
    return LIGHT.read_text()


def ink_text(svg: str, colour: str) -> str:
    """Inkscape drops a fill that equals the default, black, from the outlined text: state it, so it can be swapped."""
    for group in re.finditer(r'<(?:g|path)\b[^>]*aria-label="[^"]*"[^>]*>', svg):
        element = group.group(0)
        if "fill:" in element:
            continue
        if 'style="' in element:
            patched = re.sub(r'style="([^"]*)"', lambda m: f'style="{m.group(1).rstrip(";")};fill:{colour}"', element, count=1)
        else:
            patched = re.sub(r"^<(\w+)", rf'<\1 style="fill:{colour}"', element)
        svg = svg.replace(element, patched, 1)
    return svg


def png(svg: pathlib.Path) -> None:
    subprocess.run(["inkscape", str(svg), "--export-type=png", "--export-width=1024", "-o", str(svg.with_suffix(".png"))],
                   check=True, capture_output=True)


light = ink_text(outline(), INK)
LIGHT.write_text(light)
DARK.write_text(light.replace(f"fill:{INK}", f"fill:{DARK_INK}"))
for file in (LIGHT, DARK):
    assert "<text" not in file.read_text(), f"{file.name} still has live text"
    png(file)
print("wrote", ", ".join(p.name for p in (LIGHT, DARK, LIGHT.with_suffix(".png"), DARK.with_suffix(".png"))))
