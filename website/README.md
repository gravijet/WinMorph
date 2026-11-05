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
