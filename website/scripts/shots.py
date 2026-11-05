"""Convert native-resolution, bilingual screenshots from the CI screenshots artifact.

    python3 website/scripts/shots.py /path/to/screenshots
"""
import sys
from pathlib import Path
from PIL import Image

LOOKS = ["win10", "win7", "xp", "win95", "macos", "ubuntu", "debian", "arch", "mint", "chromeos", "nova", "nexus", "terminal", "zen", "gaming"]
LIVE = ["matrix", "starfield", "plexus", "radar", "bokeh", "zen-desk", "gaming-desk"]
OUT = Path(__file__).resolve().parent.parent / "public"


def save(source, target, widths):
    with Image.open(source) as im:
        im = im.convert("RGB")
        for width, suffix in widths:
            width = width or im.width
            # Never disguise a low-resolution source by enlarging it.
            if im.width < width:
                raise ValueError(f"{source}: need {width}px, got {im.width}px")
            image = im if width == im.width else im.resize((width, round(im.height * width / im.width)), Image.Resampling.LANCZOS)
            image.save(target.with_name(target.name + suffix + ".webp"), "WEBP", quality=94, method=6)


def main(source):
    # Check the whole artifact first, so an incomplete run doesn't partly replace the site.
    required = [source / "walls" / f"{look}.png" for look in LOOKS + ["win11"]]
    for lang in ("de", "en"):
        required += [source / lang / f"{name}.png" for look in LOOKS for name in (look, look + "-menu", "lock-" + look)]
        required += [source / lang / f"live-{scene}.png" for scene in LIVE]
        required.append(source / lang / "app.png")
    for file in required:
        with Image.open(file) as im:
            minimum = 3840 if file.parent.name == "walls" else 2600 if file.stem == "app" else 3200
            if im.width < minimum:
                raise ValueError(f"{file}: expected at least {minimum}px, got {im.width}px")
    for lang in ("de", "en"):
        target = OUT / "shots" / lang
        target.mkdir(parents=True, exist_ok=True)
        for look in LOOKS:
            for name in (look, look + "-menu"):
                save(source / lang / f"{name}.png", target / f"look-{name}", [(800, "-sm"), (1600, ""), (3200, "-2x")])
            save(source / lang / f"lock-{look}.png", target / f"lock-{look}", [(1600, ""), (3200, "-2x")])
        for scene in LIVE:
            save(source / lang / f"live-{scene}.png", target / f"live-{scene}", [(480, "-sm"), (1600, ""), (3200, "-2x")])
        save(source / lang / "app.png", target / "app", [(0, "")])
    for look in LOOKS + ["win11"]:
        save(source / "walls" / f"{look}.png", OUT / "shots" / f"wall-{look}", [(480, "-sm"), (1600, ""), (3840, "-2x")])
    # Text-free social preview: the same logo as the site and application.
    share = Image.new("RGB", (1200, 630), "#0c1020")
    logo = Image.open(OUT / "logo.png").convert("RGBA")
    share.paste(logo, (344, 59), logo)
    share.save(OUT / "og.png", optimize=True)
    print("Updated sharp DE/EN screenshots and social preview.")


if __name__ == "__main__":
    main(Path(sys.argv[1]))
