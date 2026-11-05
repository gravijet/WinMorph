# WinMorph website

Static site plus a small Cloudflare Worker (`src/worker.js`) for `winmorph.benjaminberger.at`. Same setup as `mysql.benjaminberger.at`.

- `public/` is the page (HTML, CSS, JS, no build step). Fonts and pictures are served from this domain only.
- `/api/release` returns the newest GitHub release (version, size, SHA-256) as JSON. WinMorph's updater (`Core/Updater.cs`) and the download button read it. It is cached at the edge for five minutes and falls back to the last known value.
- `/download/setup` streams `WinMorph-Setup-<version>.exe` of the newest release.

Pictures: every CI run of `Build & Release` uploads fresh screenshots of all looks. To refresh the site's pictures:

```console
gh run download <run-id> -R gravijet/WinMorph -n screenshots -D /tmp/shots
python3 website/scripts/shots.py /tmp/shots
```

Deploy: automatic via `.github/workflows/website.yml` once the repository secrets `CLOUDFLARE_API_TOKEN` and `CLOUDFLARE_ACCOUNT_ID` exist (the same values as in EasyMySQL). By hand:

```console
cd website
npm install
CLOUDFLARE_API_TOKEN=... npx wrangler deploy
```
