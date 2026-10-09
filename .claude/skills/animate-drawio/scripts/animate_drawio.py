#!/usr/bin/env python3
"""Turn one page of a .drawio file into a stepped animated GIF and SVG.

  animate_drawio.py FILE PAGE --list
  animate_drawio.py FILE PAGE --steps "28;12;6;6,26,15,22;8" --out-dir DIR [--name NAME] [--seconds 3] [--id-prefix P] [--color 000099]

Steps are separated by ';', edge ids inside a step by ','. An id is a full cell id,
or a suffix that is joined to --id-prefix (e.g. prefix "GqMIgh-" + "28").
--color recolours the edges of the active step (hex, with or without '#').
Edges sharing the colour of any step edge are dimmed when not in the active step.
"""
import argparse, os, re, shutil, subprocess, sys, tempfile
import xml.etree.ElementTree as ET
from PIL import Image

DRAWIO_CANDIDATES = ["/Applications/draw.io.app/Contents/MacOS/draw.io", "drawio", "draw.io"]
DIM = "#D0D0E0"


def find_drawio():
    for c in DRAWIO_CANDIDATES:
        p = c if os.path.isabs(c) else shutil.which(c)
        if p and os.path.exists(p):
            return p
    sys.exit("draw.io CLI not found. Install it: brew install --cask drawio")


def style_of(c, key):
    m = re.search(rf"(?:^|;){key}=([^;]*)", c.get("style", ""))
    return m.group(1) if m else None


def load_page(src, page):
    tree = ET.parse(src)
    names = [d.get("name") for d in tree.getroot()]
    if page not in names:
        sys.exit(f"Page {page!r} not found. Pages: {names}")
    return tree, names.index(page) + 1


def single_page(tree, page):
    for d in list(tree.getroot()):
        if d.get("name") != page:
            tree.getroot().remove(d)
    return tree


def edges(tree):
    return [c for c in tree.getroot().iter("mxCell") if c.get("edge") == "1"]


def cmd_list(src, page):
    tree, _ = load_page(src, page)
    cells = {c.get("id"): c for c in tree.getroot().iter("mxCell")}
    def label(i):
        c = cells.get(i)
        if c is None:
            return "(free end)"
        g = c.find("mxGeometry")
        v = re.sub("<[^>]+>", "", c.get("value") or "").strip()
        return f"{i} [{v or style_of(c, 'image') or style_of(c, 'shape') or 'node'}] @({g.get('x')},{g.get('y')})"
    for e in edges(tree):
        print(f"{e.get('id')}: {label(e.get('source'))} -> {label(e.get('target'))}"
              f"  color={style_of(e, 'strokeColor')} dashed={style_of(e, 'dashed')}")


def run_drawio(exe, f, fmt, out, *extra):
    subprocess.run([exe, "-x", "-f", fmt, "-o", out, *extra, f], check=True, capture_output=True)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("file"); ap.add_argument("page")
    ap.add_argument("--list", action="store_true")
    ap.add_argument("--steps"); ap.add_argument("--out-dir"); ap.add_argument("--name")
    ap.add_argument("--seconds", type=float, default=3.0)
    ap.add_argument("--id-prefix", default="")
    ap.add_argument("--color")
    a = ap.parse_args()
    if a.list:
        return cmd_list(a.file, a.page)
    if not (a.steps and a.out_dir):
        ap.error("--steps and --out-dir are required")

    steps = [[(s if s in {c.get("id") for c in edges(load_page(a.file, a.page)[0])} else a.id_prefix + s)
              for s in grp.split(",")] for grp in a.steps.split(";")]
    exe, n = find_drawio(), len(steps)
    name = a.name or a.page.lower()
    tmp = tempfile.mkdtemp()
    ids = {c.get("id") for c in edges(load_page(a.file, a.page)[0])}
    bad = {e for g in steps for e in g} - ids
    if bad:
        sys.exit(f"Unknown edge ids {sorted(bad)}. Run with --list.")
    used = {e for g in steps for e in g}

    def build(active, animate):
        tree, _ = load_page(a.file, a.page)
        single_page(tree, a.page)
        by_id = {c.get("id"): c for c in edges(tree)}
        colors = {style_of(by_id[i], "strokeColor") for i in used}
        for i, c in by_id.items():
            col = style_of(c, "strokeColor")
            if i in active:
                if a.color:
                    hexc = "#" + a.color.lstrip("#")
                    st = c.get("style")
                    c.set("style", re.sub(r"strokeColor=[^;]*", f"strokeColor={hexc}", st)
                          if "strokeColor=" in st else st + f";strokeColor={hexc}")
                if animate:
                    c.set("style", c.get("style") + ";flowAnimation=1")
            elif col in colors:
                c.set("style", re.sub(r"strokeColor=[^;]*", f"strokeColor={DIM}", c.get("style")))
        p = os.path.join(tmp, "v.drawio"); tree.write(p); return p

    frames = []
    for i, g in enumerate(steps):
        out = os.path.join(tmp, f"s{i}.png")
        run_drawio(exe, build(set(g), True), "png", out, "--scale", "1", "-b", "10")
        frames.append(Image.open(out).convert("RGB").quantize(256, dither=Image.Dither.NONE))

    svg_src = os.path.join(tmp, "all.svg")
    run_drawio(exe, build(used, True), "svg", svg_src)
    svg = open(svg_src).read()

    css, total = "", n * a.seconds
    for e in sorted(used):
        on = [i for i, g in enumerate(steps) if e in g]
        stops = ["0%{opacity:.15}"]
        for i in range(n):
            lo, hi = i * 100 / n, (i + 1) * 100 / n
            o = 1 if i in on else .15
            stops.append(f"{lo:.2f}%{{opacity:{o}}} {hi - .01:.2f}%{{opacity:{o}}}")
        kid = "cp-" + re.sub(r"\W", "_", e)
        css += f"@keyframes {kid}{{{' '.join(stops)} 100%{{opacity:.15}}}}\n"
        css += f'g[data-cell-id="{e}"]{{animation:{kid} {total:g}s infinite}}\n'
    svg = svg.replace("</svg>", f"<style>{css}</style></svg>", 1)

    os.makedirs(a.out_dir, exist_ok=True)
    frames[0].save(os.path.join(a.out_dir, f"{name}.gif"), save_all=True, append_images=frames[1:],
                   duration=int(a.seconds * 1000), loop=0, optimize=True)
    open(os.path.join(a.out_dir, f"{name}.svg"), "w").write(svg)
    print(f"wrote {a.out_dir}/{name}.gif and .svg ({n} steps x {a.seconds:g}s); frames in {tmp}")


if __name__ == "__main__":
    main()
