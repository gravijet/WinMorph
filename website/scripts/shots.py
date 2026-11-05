"""Turns the screenshots of a CI run (artifact "screenshots") into the pictures of the website.

    gh run download <run-id> -R gravijet/WinMorph -n screenshots -D /tmp/shots
    python3 website/scripts/shots.py /tmp/shots
"""
import sys
from pathlib import Path
from PIL import Image

src = Path(sys.argv[1])
out = Path(__file__).resolve().parent.parent / "public" / "shots"
out.mkdir(parents=True, exist_ok=True)


def save(name, target, widths, q=80):
    f = src / name
    if not f.exists():
        print("missing", name)
        return None
    im = Image.open(f).convert("RGB")
    for w, suffix in widths:
        h = round(im.height * w / im.width) if w else im.height
        (im.resize((w, h), Image.LANCZOS) if w else im).save(out / f"{target}{suffix}.webp", "WEBP", quality=q, method=6)
    return im


# Windows 11 is the stock look without a design shell, so it has no picture
looks = ["win10", "win7", "xp", "win95", "macos", "ubuntu", "debian", "arch", "mint", "chromeos", "nova", "nexus", "terminal", "zen", "gaming"]
for l in looks:
    save(f"{l}.png", f"look-{l}", [(1600, ""), (800, "-sm")])
    save(f"{l}-menu.png", f"look-{l}-menu", [(1600, ""), (800, "-sm")])
    save(f"lock-{l}.png", f"lock-{l}", [(1200, "")])
# wallpapers: the generated one of every look, and stills of the live ones
for l in looks + ["win11"]:
    save(f"walls/{l}.png", f"wall-{l}", [(1600, ""), (480, "-sm")])
for w in ["matrix", "starfield", "plexus", "radar", "bokeh", "zen-desk", "gaming-desk"]:
    save(f"live-{w}.png", f"live-{w}", [(1600, ""), (480, "-sm")])
for lang in ["de", "en"]:
    save(f"app-terminal-{lang}.png", f"app-{lang}", [(0, "")], q=88)

# share picture: four looks on black, 2 × 2
from PIL import ImageOps
names = ["lock-terminal", "macos", "xp", "zen"]
if all((src / f"{n}.png").exists() for n in names):
    og = Image.new("RGB", (1200, 630), (0, 0, 0))
    for i, n in enumerate(names):
        t = ImageOps.fit(Image.open(src / f"{n}.png").convert("RGB"), (596, 311), Image.LANCZOS)
        og.paste(t, ((i % 2) * 604, (i // 2) * 319))
    og.save(out.parent / "og.png", optimize=True)
print("done:", len(list(out.glob("*.webp"))), "pictures")
