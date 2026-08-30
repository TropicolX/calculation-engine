#!/usr/bin/env python3
"""Renders every Markdown document in the repository to PDF.

    pip install markdown weasyprint
    python3 tools/docs-pdf/build_pdfs.py

Mermaid diagrams are pre-rendered to PNG with mermaid-cli (fetched by npx, the
same way docs/uml/render.sh does it in the uptime-monitor project) because
WeasyPrint does not execute JavaScript. Everything else is plain
Markdown -> HTML -> PDF, so the output is deterministic and needs no browser.
"""
import hashlib
import os
import re
import subprocess
import sys
import tempfile

import markdown
from weasyprint import HTML

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), os.pardir, os.pardir))
OUT_DIR = os.path.join(REPO, "docs", "pdf")
DIAGRAM_DIR = os.path.join(OUT_DIR, "diagrams")

# source path -> output basename (flattened, so nothing collides)
DOCUMENTS = [
    ("README.md",                     "README"),
    ("tools/README.md",               "tools-README"),
    ("docs/adt-specifications.md",    "adt-specifications"),
    ("docs/ai-collaboration-log.md",  "ai-collaboration-log"),
    ("docs/benchmarks.md",            "benchmarks"),
    ("docs/design-portfolio.md",      "design-portfolio"),
    ("docs/grammar.md",               "grammar"),
    ("docs/reflection.md",            "reflection"),
    ("docs/testing.md",               "testing"),
    ("docs/critique/critique.md",     "critique"),
    ("docs/critique/transcript.md",   "critique-transcript"),
]

MERMAID = re.compile(r"^```mermaid[ \t]*\n(.*?)^```[ \t]*$", re.S | re.M)

CSS = """
@page {
    size: A4;
    margin: 20mm 18mm 20mm 18mm;
    @top-left   { content: string(doctitle); font: 8pt "Helvetica Neue", Helvetica, sans-serif; color: #7a828c; }
    @bottom-right { content: counter(page); font: 8pt "Helvetica Neue", Helvetica, sans-serif; color: #7a828c; }
}
@page :first { @top-left { content: none; } }

body { font: 10.5pt/1.55 "Georgia", "Times New Roman", serif; color: #1a1d24; }

h1 { string-set: doctitle content();
     font: 700 22pt/1.25 "Helvetica Neue", Helvetica, sans-serif;
     color: #1f4e79; margin: 0 0 4pt; padding-bottom: 6pt;
     border-bottom: 2px solid #1f4e79; }
h2 { font: 700 15pt/1.3 "Helvetica Neue", Helvetica, sans-serif; color: #1f4e79;
     margin: 20pt 0 6pt; break-after: avoid; }
h3 { font: 700 12pt/1.35 "Helvetica Neue", Helvetica, sans-serif; color: #2b3038;
     margin: 15pt 0 5pt; break-after: avoid; }
h4, h5, h6 { font: 700 10.5pt/1.35 "Helvetica Neue", Helvetica, sans-serif;
     color: #2b3038; margin: 12pt 0 4pt; break-after: avoid; }
p { margin: 0 0 7pt; text-align: justify; }
a { color: #1f4e79; text-decoration: none; word-break: break-word; }

ul, ol { margin: 0 0 8pt; padding-left: 18pt; }
li { margin-bottom: 3pt; }

code { font: 9pt "Menlo", "Consolas", monospace; background: #f1f3f6;
       padding: 0.5pt 3pt; border-radius: 3px; color: #8a2f2f;
       word-break: break-word; }
pre { background: #f6f7f9; border: 0.5pt solid #d3d8df; border-left: 2.5pt solid #1f4e79;
      border-radius: 3px; padding: 8pt 10pt; margin: 0 0 10pt;
      break-inside: avoid; white-space: pre-wrap; word-wrap: break-word; }
pre code { font-size: 8.2pt; background: none; padding: 0; color: #1a1d24;
           line-height: 1.42; }

blockquote { margin: 0 0 9pt; padding: 5pt 0 5pt 11pt;
             border-left: 2.5pt solid #b9c2cd; color: #4b5361; font-style: italic; }
blockquote p { margin-bottom: 4pt; }

/* Automatic layout, so a column of long words gets the width it needs.
   Fixed layout split words mid-token ("recalculati / on"). */
table { width: 100%; border-collapse: collapse; margin: 0 0 11pt;
        font-size: 8.6pt; table-layout: auto; }
/* Headers never wrap, so a narrow numeric column cannot produce "Verdi / ct". */
th { background: #1f4e79; color: #fff; text-align: left; font-weight: 700;
     font-family: "Helvetica Neue", Helvetica, sans-serif; white-space: nowrap; }
/* break-word only splits a word that cannot fit on a line of its own; "anywhere"
   also lets the column shrink below word width, which is what broke "PASS". */
th, td { border: 0.5pt solid #cbd1da; padding: 4pt 5pt; vertical-align: top;
         overflow-wrap: break-word; hyphens: none; }
tbody tr:nth-child(even) { background: #f6f8fb; }
thead { display: table-header-group; }
tr { break-inside: avoid; }

img { max-width: 100%; display: block; margin: 8pt auto; }
hr { border: 0; border-top: 0.5pt solid #d3d8df; margin: 14pt 0; }
"""

TEMPLATE = """<!DOCTYPE html>
<html><head><meta charset="utf-8"><title>{title}</title>
<style>{css}</style></head><body>{body}</body></html>"""


def render_mermaid(source, index):
    """Renders one mermaid block to PNG and returns its path, or None."""
    if not os.path.isdir(DIAGRAM_DIR):
        os.makedirs(DIAGRAM_DIR)
    digest = hashlib.sha1(source.encode("utf-8")).hexdigest()[:12]
    png = os.path.join(DIAGRAM_DIR, "diagram-%02d-%s.png" % (index, digest))
    if os.path.exists(png):
        return png

    with tempfile.NamedTemporaryFile("w", suffix=".mmd", delete=False) as handle:
        handle.write(source)
        mmd = handle.name
    try:
        subprocess.run(
            ["npx", "-y", "@mermaid-js/mermaid-cli@11",
             "-i", mmd, "-o", png, "-t", "neutral", "-b", "white", "-s", "2", "--quiet"],
            check=True, capture_output=True, timeout=180)
        return png
    except Exception as error:
        print("      ! mermaid render failed: %s" % error)
        return None
    finally:
        os.unlink(mmd)


def substitute_diagrams(text, counter):
    """Replaces ```mermaid fences with image references."""
    def replace(match):
        counter[0] += 1
        png = render_mermaid(match.group(1), counter[0])
        if not png:
            # Leave the source visible rather than silently dropping the diagram.
            return "```\n" + match.group(1) + "```"
        return "\n![](%s)\n" % png.replace(" ", "%20")
    return MERMAID.sub(replace, text)


def build(source_rel, out_base, counter):
    source_path = os.path.join(REPO, source_rel)
    with open(source_path, encoding="utf-8") as handle:
        text = handle.read()

    diagrams_before = counter[0]
    text = substitute_diagrams(text, counter)

    # A document with no level-1 heading gets one, so the running header and the
    # PDF title are never empty.
    if not re.search(r"^# ", text, re.M):
        text = "# %s\n\n%s" % (out_base.replace("-", " ").title(), text)

    html_body = markdown.markdown(text, extensions=[
        "tables", "fenced_code", "toc", "attr_list", "sane_lists", "def_list", "md_in_html"])

    title_match = re.search(r"^# (.+)$", text, re.M)
    title = title_match.group(1).strip() if title_match else out_base

    html = TEMPLATE.format(title=title, css=CSS, body=html_body)
    out_path = os.path.join(OUT_DIR, out_base + ".pdf")

    # base_url is the source file's folder so relative images resolve.
    HTML(string=html, base_url=os.path.dirname(source_path) + os.sep).write_pdf(out_path)

    made = counter[0] - diagrams_before
    return out_path, made


def main():
    if not os.path.isdir(OUT_DIR):
        os.makedirs(OUT_DIR)

    counter = [0]
    results = []
    for source_rel, out_base in DOCUMENTS:
        if not os.path.exists(os.path.join(REPO, source_rel)):
            print("  skip (missing): %s" % source_rel)
            continue
        print("  %s" % source_rel)
        path, diagrams = build(source_rel, out_base, counter)
        size = os.path.getsize(path)
        results.append((out_base, size, diagrams))
        print("      -> %-24s %6.1f KB%s"
              % (os.path.basename(path), size / 1024.0,
                 "  (%d diagram%s)" % (diagrams, "" if diagrams == 1 else "s") if diagrams else ""))

    print("\n%d PDF(s) written to docs/pdf/" % len(results))
    print("%d mermaid diagram(s) rendered" % counter[0])


if __name__ == "__main__":
    main()
