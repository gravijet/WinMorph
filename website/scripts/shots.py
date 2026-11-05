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
for s in ["matrix", "matrix-hud", "starfield", "plexus", "radar", "bokeh", "zen-desk", "gaming-desk", "hacker-desk"]:
    save(f"live-{s}.png", f"live-{s}", [(800, "")])
for lang in ["de", "en"]:
    save(f"app-terminal-{lang}.png", f"app-{lang}", [(0, "")], q=88)
save("app-tasks.png", "hacker-tasks", [(0, "")], q=88)
save("app-files.png", "hacker-files", [(0, "")], q=88)

# share picture: four looks on black
tiles = [Image.open(src / f"{l}.png").convert("RGB").resize((592, 333), Image.LANCZOS) for l in ["terminal", "macos", "xp", "zen"] if (src / f"{l}.png").exists()]
if len(tiles) == 4:
    og = Image.new("RGB", (1200, 630), (0, 0, 0))
    for i, t in enumerate(tiles):
        og.paste(t, (8 + (i % 2) * 600, (i // 2) * 297 - 18 if i // 2 else -36))
    og.save(out.parent / "og.png", optimize=True)
print("done:", len(list(out.glob("*.webp"))), "pictures")
