#!/usr/bin/env python3
"""Build the two GitHub-readable wiki previews from canonical race dossiers."""
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[2]
DOCS = ROOT / "GameDoc"
OUT = DOCS / "wiki"
PAGES = {"race_sinapty.md": ("sinapty.md", "Синапты"),
         "race_ksenopody.md": ("ksenopody.md", "Ксеноподы")}
TERMS = [
    (r"синапт(?:ы|ов|ам|ами|ах|а)?", "sinapty.md"),
    (r"ксенопод(?:ы|ов|ам|ами|ах|а)?", "ksenopody.md"),
    (r"грондар(?:ы|ов|ам|ами|ах|а)?", "../race_grondary.md"),
    (r"этериан(?:е|ов|ам|ами|ах|ина)?", "../race_etheriane.md"),
    (r"тарна(?:и|ев|ям|ями|ях|й)?", "../race_propovedniki.md"),
    (r"хамари", "../race_hamari.md"),
    (r"аэлит(?:ы|ов|ам|ами|ах)?", "../race_aelity.md"),
    (r"дейми", "../race_deymi.md"),
    (r"вравши", "../race_vravshi.md"),
    (r"заргаш(?:и|ей|ам|ами|ах)?", "../race_zargashi.md"),
    (r"аэтари", "../race_aetari.md"),
    (r"Лингв(?:а|ы|е|у|ой)", "../reference_instituty_sodruzhestva.md"),
    (r"гиперсфер(?:а|ы|е|у|ой)", "../reference_gipersfera.md"),
]
LINK = re.compile(r"(!?\[[^\]\n]*\]\([^\)\n]+\)|<[^>]+>|`[^`]*`)")

def remap(match):
    label, target = match.group(1), match.group(2)
    if target.startswith(("#", "https:", "http:", "mailto:")):
        return match.group(0)
    path, sep, anchor = target.partition("#")
    target = PAGES[path][0] if path in PAGES else "../" + path
    return f"[{label}]({target}{sep}{anchor})"

def crosslinks(text, own):
    # Existing links, HTML anchors and code are never linked a second time.
    chunks = LINK.split(text)
    used = set()
    for i in range(0, len(chunks), 2):
        for pattern, target in TERMS:
            if target == own or target in used:
                continue
            rx = re.compile(r"(?<![\w])" + pattern + r"(?![\w])", re.I)
            if rx.search(chunks[i]):
                chunks[i] = rx.sub(lambda m: f"[{m.group(0)}]({target})", chunks[i], count=1)
                used.add(target)
    return "".join(chunks)

def build(source, filename, title):
    text = (DOCS / source).read_text()
    text = re.sub(r"\A---\n.*?\n---\n", "", text, flags=re.S).strip()
    text = re.sub(r"<!-- section-status: [^>]+-->\n?", "", text)
    text = re.sub(r"\[([^\]\n]+)\]\(([^\)\n]+)\)", remap, text)
    first, body = text.split("\n", 1)
    # Extract the original card into a compact infobox without changing its facts.
    card = re.search(r'<a id="overview.card"></a>\s*### [^\n]+\n+(.*?)(?=\n<a id=)', body, re.S)
    rows = []
    for line in card.group(1).splitlines():
        if line.startswith("- ") and ": " in line:
            key, value = line[2:].split(": ", 1)
            rows.append("| " + key + " | " + value.replace("|", "\\|") + " |")
    body = body[:card.start()] + '<a id="overview.card"></a>\n\n' + body[card.end():]
    headings = re.findall(r'<a id="([^"]+)"></a>\s*## ([^\n]+)', body)
    toc = "\n".join(f"- [{label}](#{anchor})" for anchor, label in headings)
    parts = body.split("\n\n")
    body = "\n\n".join(crosslinks(p, filename) if not p.lstrip().startswith(("#", "<a ")) else p for p in parts)
    nav = "[Энциклопедия](README.md) · [Синапты](sinapty.md) · [Ксеноподы](ksenopody.md) · [История](../reference_period_2500_2800.md) · [Все досье](../README.md#актуальные-досье-рас)"
    cardtext = "| Сведения | Описание |\n|---|---|\n" + "\n".join(rows)
    result = f"{nav}\n\n{first}\n\n> Пробная вики-страница. Полный текст из [исходного досье](../{source}); численные сводки относятся к указанным в тексте эпохам.\n\n{cardtext}\n\n## Содержание\n\n{toc}\n\n---\n\n{body.strip()}\n\n---\n\n{nav}\n"
    (OUT / filename).write_text(result)

if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    for source, (filename, title) in PAGES.items():
        build(source, filename, title)
        print(f"Built GameDoc/wiki/{filename}")
