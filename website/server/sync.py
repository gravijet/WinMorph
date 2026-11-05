#!/usr/bin/env python3
"""Keeps winmorph.benjaminberger.at current on this server (winmorph-sync.timer runs it every five minutes).

- pulls the site (repo/website/public) from GitHub
- mirrors the newest release: data/WinMorph-Setup.exe is /download/setup, data/release.json is /api/release,
  which WinMorph's updater reads (Core/Updater.cs). The setup is only published after its SHA-256 matched.
"""
import hashlib
import json
import os
import re
import subprocess
import urllib.request

REPO = "gravijet/WinMorph"
BASE = os.environ.get("WINMORPH_HOME", "/var/www/winmorph")
DATA = os.path.join(BASE, "data")


def get(url, accept):
    req = urllib.request.Request(url, headers={"User-Agent": "winmorph-site", "Accept": accept})
    with urllib.request.urlopen(req, timeout=60) as r:
        return r.read()


def put(path, data):
    tmp = path + ".tmp"
    with open(tmp, "wb") as f:
        f.write(data)
    os.replace(tmp, path)


def sync_release():
    d = json.loads(get(f"https://api.github.com/repos/{REPO}/releases/latest", "application/vnd.github+json"))
    version = str(d.get("tag_name", "")).lstrip("v")
    if not re.fullmatch(r"\d+\.\d+\.\d+", version) or d.get("draft") or d.get("prerelease"):
        return
    name = f"WinMorph-Setup-{version}.exe"
    asset = next((a for a in d.get("assets", []) if a.get("name") == name), None)
    if not asset:
        return
    sha = (asset.get("digest") or "").removeprefix("sha256:").lower()
    if not re.fullmatch(r"[0-9a-f]{64}", sha):
        sums = get(f"https://github.com/{REPO}/releases/download/v{version}/SHA256SUMS.txt", "text/plain").decode()
        sha = next((l.split()[0].lower() for l in sums.splitlines() if l.strip().endswith(name)), "")
    if not re.fullmatch(r"[0-9a-f]{64}", sha):
        raise SystemExit(f"no checksum for {name}")

    exe, meta = os.path.join(DATA, "WinMorph-Setup.exe"), os.path.join(DATA, "release.json")
    try:
        with open(meta) as f:
            if json.load(f)["files"]["setup"]["sha256"] == sha and os.path.exists(exe):
                return  # already the newest
    except (OSError, ValueError, KeyError):
        pass
    data = get(asset["browser_download_url"], "application/octet-stream")
    if hashlib.sha256(data).hexdigest() != sha:
        raise SystemExit(f"checksum mismatch for {name}, keeping the old release")
    put(exe, data)  # the file first, so release.json never names a setup that is not there yet
    rel = {"version": version, "published": d.get("published_at"),
           "files": {"setup": {"name": name, "url": "/download/setup", "size": len(data), "sha256": sha}}}
    put(meta, json.dumps(rel).encode())
    print(f"release {version} published ({len(data)} bytes)")


if __name__ == "__main__":
    os.makedirs(DATA, exist_ok=True)
    subprocess.run(["git", "-C", os.path.join(BASE, "repo"), "pull", "-q", "--ff-only"], timeout=120, check=False)
    sync_release()
