"""Builds ClaudeLearning.pdf from ClaudeLearning.md (same folder).

Usage:  python docs/learning/build_pdf.py
Needs:  pip install reportlab
Supports a simple Markdown subset: # ## ### headings, paragraphs, - bullets,
1. numbered items, > notes, ``` code blocks, | tables |, **bold**, *italic*, `code`.
"""
import os, re, sys
from datetime import date

try:
    from reportlab.lib.pagesizes import letter
    from reportlab.platypus import SimpleDocTemplate, Paragraph, Spacer, Preformatted, Table, TableStyle
    from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
    from reportlab.lib import colors
    from reportlab.lib.units import inch
    from reportlab.pdfbase import pdfmetrics
    from reportlab.pdfbase.ttfonts import TTFont
except ImportError:
    sys.exit("reportlab is not installed. Run: pip install reportlab")

HERE = os.path.dirname(os.path.abspath(__file__))
SRC, OUT = os.path.join(HERE, "ClaudeLearning.md"), os.path.join(HERE, "ClaudeLearning.pdf")

# Use a Unicode TrueType font when one is available (accents, arrows); fall back to Helvetica.
FONT, BOLD, MONO = "Helvetica", "Helvetica-Bold", "Courier"
for reg, bold in [(r"C:\Windows\Fonts\arial.ttf", r"C:\Windows\Fonts\arialbd.ttf"),
                  ("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf")]:
    if os.path.exists(reg) and os.path.exists(bold):
        pdfmetrics.registerFont(TTFont("Body", reg)); pdfmetrics.registerFont(TTFont("Body-Bold", bold))
        pdfmetrics.registerFontFamily("Body", normal="Body", bold="Body-Bold", italic="Body", boldItalic="Body-Bold")
        FONT, BOLD = "Body", "Body-Bold"
        break

def clean(text):
    # Characters fonts often lack: map the common ones, drop emoji.
    for a, b in {"\u2192": "->", "\u2190": "<-", "\u2705": "[x]", "\u2714": "[x]", "\u274c": "[ ]", "\u2795": "+"}.items():
        text = text.replace(a, b)
    if FONT == "Helvetica":
        text = text.encode("cp1252", "replace").decode("cp1252")
    return re.sub("[\U00010000-\U0010FFFF]", "", text)

def inline(text):
    text = clean(text).replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")
    # Protect `code` spans first so * and ** inside them are left alone.
    spans = []
    def keep(m):
        spans.append(m.group(1)); return f"\x00{len(spans) - 1}\x00"
    text = re.sub(r"`([^`]+)`", keep, text)
    text = re.sub(r"\*\*([^*]+)\*\*", r"<b>\1</b>", text)
    text = re.sub(r"(?<![*\w])\*([^*\s][^*]*)\*(?![*\w])", r"<i>\1</i>", text)
    return re.sub("\x00(\\d+)\x00", lambda m: f"<font face='{MONO}'>{spans[int(m.group(1))]}</font>", text)

ACCENT = colors.HexColor("#1F4E79")
ss = getSampleStyleSheet()
S = {
    "title": ParagraphStyle("t", parent=ss["Title"], fontName=BOLD, textColor=ACCENT, fontSize=22, leading=28),
    "h1": ParagraphStyle("h1", parent=ss["Heading1"], fontName=BOLD, textColor=ACCENT, fontSize=15, spaceBefore=12, spaceAfter=5),
    "h2": ParagraphStyle("h2", parent=ss["Heading2"], fontName=BOLD, textColor=ACCENT, fontSize=12.5, spaceBefore=9, spaceAfter=3),
    "body": ParagraphStyle("b", parent=ss["Normal"], fontName=FONT, fontSize=10.3, leading=14.2, spaceAfter=5),
    "note": ParagraphStyle("n", parent=ss["Normal"], fontName=FONT, fontSize=10.3, leading=14.2, backColor=colors.HexColor("#EEF4FA"), borderPadding=6, spaceBefore=4, spaceAfter=9),
    "code": ParagraphStyle("c", fontName=MONO, fontSize=8.4, leading=10.8, backColor=colors.HexColor("#F4F4F4"), borderPadding=5, spaceBefore=3, spaceAfter=8),
    "cell": ParagraphStyle("cell", parent=ss["Normal"], fontName=FONT, fontSize=9.2, leading=11.6),
    "head": ParagraphStyle("head", parent=ss["Normal"], fontName=BOLD, fontSize=9.2, leading=11.6, textColor=colors.white),
}
S["bullet"] = ParagraphStyle("bl", parent=S["body"], leftIndent=14, bulletIndent=4, spaceAfter=3)

def table(rows, width):
    rows = [[c.strip() for c in r.strip().strip("|").split("|")] for r in rows if not re.match(r"^\s*\|[\s:\-|]+\|\s*$", r)]
    n = max(len(r) for r in rows)
    data = [[Paragraph(inline(r[i] if i < len(r) else ""), S["head"] if k == 0 else S["cell"]) for i in range(n)] for k, r in enumerate(rows)]
    t = Table(data, colWidths=[width / n] * n, repeatRows=1)
    t.setStyle(TableStyle([("BACKGROUND", (0, 0), (-1, 0), ACCENT), ("GRID", (0, 0), (-1, -1), 0.4, colors.HexColor("#BBBBBB")),
                           ("VALIGN", (0, 0), (-1, -1), "TOP"), ("ROWBACKGROUNDS", (0, 1), (-1, -1), [colors.white, colors.HexColor("#F7F9FC")])]))
    return t

def build():
    lines = open(SRC, encoding="utf-8").read().splitlines()
    story, para, i, width = [], [], 0, letter[0] - 1.6 * inch
    def flush():
        if para:
            story.append(Paragraph(inline(" ".join(para)), S["body"])); para.clear()
    while i < len(lines):
        line = lines[i]
        if line.startswith("```"):
            flush(); block = []; i += 1
            while i < len(lines) and not lines[i].startswith("```"):
                block.append(clean(lines[i])); i += 1
            story.append(Preformatted("\n".join(block), S["code"]))
        elif line.startswith("|"):
            flush(); rows = []
            while i < len(lines) and lines[i].startswith("|"):
                rows.append(lines[i]); i += 1
            story.append(table(rows, width)); story.append(Spacer(1, 6)); continue
        elif line.startswith("# "): flush(); story.append(Paragraph(inline(line[2:]), S["title"]))
        elif line.startswith("## "): flush(); story.append(Paragraph(inline(line[3:]), S["h1"]))
        elif line.startswith("### "): flush(); story.append(Paragraph(inline(line[4:]), S["h2"]))
        elif re.match(r"^\s*[-*] ", line): flush(); story.append(Paragraph(inline(re.sub(r"^\s*[-*] ", "", line)), S["bullet"], bulletText="\u2022"))
        elif re.match(r"^\d+\. ", line):
            flush(); n, rest = line.split(". ", 1); story.append(Paragraph(inline(rest), S["bullet"], bulletText=n + "."))
        elif line.startswith("> "): flush(); story.append(Paragraph(inline(line[2:]), S["note"]))
        elif not line.strip(): flush()
        else: para.append(line.strip())
        i += 1
    flush()
    def footer(c, d):
        c.saveState(); c.setFont(FONT, 8); c.setFillColor(colors.HexColor("#888888"))
        c.drawString(0.8 * inch, 0.5 * inch, f"Claude Learning - built {date.today().isoformat()}")
        c.drawRightString(letter[0] - 0.8 * inch, 0.5 * inch, f"Page {d.page}"); c.restoreState()
    SimpleDocTemplate(OUT, pagesize=letter, leftMargin=0.8 * inch, rightMargin=0.8 * inch, topMargin=0.75 * inch,
                      bottomMargin=0.8 * inch, title="Claude Learning").build(story, onFirstPage=footer, onLaterPages=footer)
    print(f"Wrote {OUT}")

if __name__ == "__main__":
    build()
