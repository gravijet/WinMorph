#!/usr/bin/env python3
"""Build public/en/index.html from public/index.html, so English has its own address for search engines.

Every data-en text and data-en-* attribute is swapped in; the German original stays in data-de / data-de-*
so app.js can switch back. server/sync.py runs this after every pull; the output is not committed.
"""
import os
import re

PUBLIC = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "public")
ATTR = {"aria": "aria-label"}  # data-en-aria translates aria-label; every other kind names its attribute


def build(html):
    expected = len(re.findall(r'\sdata-en="', html))
    count = 0

    def text(m):
        nonlocal count
        count += 1
        tag, attrs, en, de = m.group(1), m.group(2), m.group(3), m.group(4)
        return f'<{tag}{attrs} data-de="{de.replace(chr(34), "&quot;")}">{en}</{tag}>'

    # app.js swaps textContent, so elements with data-en hold plain text only
    html = re.sub(r'<([a-z0-9]+)\b([^>]*?\sdata-en="([^"]*)"[^>]*)>([^<]*)</\1>', text, html)
    if count != expected:
        raise SystemExit(f"en.py: swapped {count} of {expected} data-en texts (nested markup?)")

    def attrs(m):
        tag = m.group(0)
        for kind, en in re.findall(r'\sdata-en-([a-z]+)="([^"]*)"', tag):
            name = ATTR.get(kind, kind)
            old = re.search(rf'(\s{name}=")([^"]*)"', tag)
            if not old:
                raise SystemExit(f"en.py: {name} missing in {tag}")
            tag = tag[:old.start()] + f'{old.group(1)}{en}" data-de-{kind}="{old.group(2)}"' + tag[old.end():]
        src_en = re.search(r'\sdata-src-en="([^"]*)"', tag)
        if src_en:
            tag = re.sub(r'(\ssrc=")([^"]*)"', lambda s: f'{s.group(1)}{src_en.group(1)}" data-src-de="{s.group(2)}"', tag, 1)
        return tag

    html = re.sub(r'<[a-z0-9]+\b[^>]*\sdata-(?:en-[a-z]+|src-en)="[^>]*>', attrs, html)
    # the screenshots in English, as app.js shows them (not data-src-de, which must keep German)
    html = re.sub(r'(\s(?:src|srcset)=")([^"]*)"', lambda m: m.group(1) + m.group(2).replace("/shots/de/", "/shots/en/") + '"', html)
    return html.replace('<html lang="de">', '<html lang="en">', 1)


if __name__ == "__main__":
    with open(os.path.join(PUBLIC, "index.html"), encoding="utf-8") as f:
        out = build(f.read())
    os.makedirs(os.path.join(PUBLIC, "en"), exist_ok=True)
    tmp = os.path.join(PUBLIC, "en", ".index.html.tmp")
    with open(tmp, "w", encoding="utf-8") as f:
        f.write(out)
    os.replace(tmp, os.path.join(PUBLIC, "en", "index.html"))  # never serve a half-written page
