# WinMorph website

`winmorph.benjaminberger.at` – a static page (`public/`, no build step) served by nginx on our own server, behind Cloudflare (wildcard record, origin certificate for `*.benjaminberger.at`).

- `/api/release` – the newest release (version, size, SHA-256) as JSON. The download button and WinMorph's updater (`Core/Updater.cs`) read it.
- `/download/setup` – the setup of the newest release, served from this server.

Both come from `server/sync.py`, which `winmorph-sync.timer` runs every five minutes as `www-data`: it pulls this repository into `/var/www/winmorph/repo` (so site changes on `main` go live by themselves) and mirrors the newest GitHub release into `/var/www/winmorph/data`. A setup is only published after its SHA-256 matched the release.

## Set up on the server (once, as root)

```console
mkdir -p /var/www/winmorph && chown www-data: /var/www/winmorph
sudo -u www-data git clone https://github.com/gravijet/WinMorph /var/www/winmorph/repo
cp /var/www/winmorph/repo/website/server/winmorph-headers.conf /etc/nginx/snippets/
cp /var/www/winmorph/repo/website/server/nginx.conf /etc/nginx/sites-available/winmorph.benjaminberger.at
ln -s /etc/nginx/sites-available/winmorph.benjaminberger.at /etc/nginx/sites-enabled/
cp /var/www/winmorph/repo/website/server/winmorph-sync.{service,timer} /etc/systemd/system/
systemctl daemon-reload && systemctl enable --now winmorph-sync.timer && systemctl start winmorph-sync.service
nginx -t && systemctl reload nginx
```

The nginx and systemd files are copied, not linked: a change to them in the repository only takes effect after copying them again.

## Pictures

Every CI run of `Build & Release` uploads fresh screenshots of all looks. To refresh the site's pictures:

```console
gh run download <run-id> -R gravijet/WinMorph -n screenshots -D /tmp/shots
python3 website/scripts/shots.py /tmp/shots
```

The artifact must contain `de/`, `en/` and `walls/`. Looks, menus and lock screens are rendered separately in each language at 3200 × 1800; wallpapers at 3840 × 2160. Text in images is translated by the application before rendering. The exporter validates every source before writing and never enlarges a smaller image. Keep the generated assets in the repository so the static deployment needs no image processing.

The SVG logo is the editable source. `logo.png`, `apple-touch-icon.png`, `og.png` and the application's `WinMorph.ico` are exports of that logo.

## Checks

```console
python3 -m unittest discover -s website/tests -p 'test_*.py'
python3 -m http.server 18765 --bind 127.0.0.1 --directory website/public
# In another terminal, with Playwright installed in your development environment:
node website/tests/site.cjs
```

Set `WINMORPH_TEST_URL` for a different server, or `CHROMIUM_PATH` for an existing Chromium installation. Browser checks cover desktop, mobile and reduced motion; language changes include accessible names, image variants, the terminal, and metadata. They also check high-density rendering, gallery controls and download-link validation.
