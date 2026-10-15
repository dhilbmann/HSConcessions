# Deploying HSConcessions

HSConcessions runs as **one self-contained .NET 10 process** on a small Linux VM, behind **Caddy** (free automatic HTTPS).
There is no database: everything lives in JSON files and images on disk, so the disk must be persistent
(don't use hosts with ephemeral file systems).

These steps use the first instance, **wildcatsnacks** (wildcatsnacks.com), as the example. Replace it with your
instance name and domain.

---

## 1. Prepare the server (once)

Any small Ubuntu/Debian VM works (1 vCPU, 1 GB RAM is plenty). Open ports 80 and 443.

```bash
sudo apt update && sudo apt install -y caddy unzip
sudo useradd --system --home /var/lib/hsconcessions --shell /usr/sbin/nologin hsconcessions
sudo mkdir -p /opt/hsconcessions /var/lib/hsconcessions /etc/hsconcessions
sudo chown hsconcessions:hsconcessions /var/lib/hsconcessions
```

## 2. Publish a self-contained build

On your development machine (needs the .NET 10 SDK), from the repo root:

```bash
# Intel/AMD VMs
dotnet publish src/HSConcessions.Web -c Release -r linux-x64 --self-contained -o publish/linux-x64
# ARM VMs (e.g. Oracle Ampere, Raspberry Pi 4/5)
dotnet publish src/HSConcessions.Web -c Release -r linux-arm64 --self-contained -o publish/linux-arm64
```

The output includes the `samples/` seed profiles and the native SkiaSharp library for Linux. Copy it to the server:

```bash
rsync -a publish/linux-x64/ you@server:/tmp/hsc/
ssh you@server 'sudo mkdir -p /opt/hsconcessions/wildcatsnacks && sudo rsync -a --delete /tmp/hsc/ /opt/hsconcessions/wildcatsnacks/ \
  && sudo chmod +x /opt/hsconcessions/wildcatsnacks/HSConcessions.Web'
```

## 3. Configure the instance

Create `/etc/hsconcessions/wildcatsnacks.env` from [`instance.env.example`](instance.env.example):

```bash
sudo cp instance.env.example /etc/hsconcessions/wildcatsnacks.env
sudo chmod 600 /etc/hsconcessions/wildcatsnacks.env
sudo nano /etc/hsconcessions/wildcatsnacks.env
```

Set at least:

| Key | Example | Notes |
|---|---|---|
| `ASPNETCORE_URLS` | `http://127.0.0.1:5080` | Use a different port per instance. |
| `Seed__Profile` | `wildcatsnacks` or `generic` | Only used on first run, when the data folder is empty. |
| `Auth__SystemAdmins__0` | `david@example.com` | System Admin email(s). Add `__1`, `__2`… for more. |
| OAuth client IDs/secrets | see §5 | |

Everything else (instance name, storage paths) is set by the systemd unit. Settings in `appsettings.json` can be overridden
with environment variables using `__` for nesting (e.g. `Features__Events=true`). A `.env` file next to the app binary
is also read, if you prefer that to the systemd `EnvironmentFile`.

## 4. The systemd unit

```bash
sudo cp hsconcessions.service /etc/systemd/system/hsconcessions@.service
sudo systemctl daemon-reload
sudo systemctl enable --now hsconcessions@wildcatsnacks
sudo systemctl status hsconcessions@wildcatsnacks
journalctl -u hsconcessions@wildcatsnacks -f      # console log; files are in /var/lib/hsconcessions/wildcatsnacks/logs
```

The unit is a template: `%i` (the instance name) sets `Instance__Name` (used in cookie, log, and backup names) and the
data paths under `/var/lib/hsconcessions/<instance>/`. Two schools on one server = two env files, two ports, two
`systemctl enable --now hsconcessions@<name>` commands.

## 5. OAuth sign-in (Google and Microsoft)

Volunteers sign in with Google or Microsoft. Only emails on the Users list (or System Admins) get in.
A provider is shown only when it is enabled **and** has a client ID and secret. Secrets go in the env file, never in git.

**Google** — <https://console.cloud.google.com> → APIs & Services:
1. OAuth consent screen: External, app name = your site title, add your domain under Authorized domains.
2. Credentials → Create credentials → OAuth client ID → *Web application*.
3. Authorized redirect URI: `https://wildcatsnacks.com/signin-google`
4. Put the values in the env file:
   `Auth__Providers__Google__ClientId=…` and `Auth__Providers__Google__ClientSecret=…`

**Microsoft** — <https://entra.microsoft.com> → App registrations → New registration:
1. Supported account types: *Accounts in any organizational directory and personal Microsoft accounts*.
2. Redirect URI (Web): `https://wildcatsnacks.com/signin-microsoft`
3. Certificates & secrets → New client secret.
4. `Auth__Providers__Microsoft__ClientId=<Application (client) ID>` and `Auth__Providers__Microsoft__ClientSecret=<secret value>`

To turn a provider off, set `Auth__Providers__Google__Enabled=false` (its button disappears).
Another OIDC provider can be added with a config section (`Auth__Providers__<Name>__Authority`, `ClientId`,
`ClientSecret`, `CallbackPath`) — no code changes to the allowlist or roles.

Restart after changing the env file: `sudo systemctl restart hsconcessions@wildcatsnacks`.

## 6. Caddy and DNS

1. At your domain registrar, create DNS records pointing at the VM's public IP:
   - `A` record: `wildcatsnacks.com` → `<IPv4>` (and `AAAA` for IPv6 if the VM has one)
   - `A` (or `CNAME`) record: `www` → the same
2. Edit [`Caddyfile`](Caddyfile) with your domain and port, then:
   ```bash
   sudo cp Caddyfile /etc/caddy/Caddyfile
   sudo systemctl reload caddy
   ```
Caddy obtains and renews the Let's Encrypt certificate automatically once DNS resolves to the server.

## 7. Set up a new school's instance

1. Pick a short instance name (e.g. `eaglesnacks`) and a free port.
2. Choose a seed profile: `generic` (neutral branding) or copy `samples/generic` to `samples/<yourschool>/` in the repo,
   edit its `settings.json`, and republish.
3. Create `/etc/hsconcessions/<name>.env` with the port, `Seed__Profile`, and your System Admin email(s); add OAuth secrets.
4. `sudo systemctl enable --now hsconcessions@<name>`, add a Caddy block, point DNS.
5. Sign in at `https://<domain>/admin` as the System Admin, then:
   - **Settings**: site base URL (used by QR codes), site title, school and mascot names, time zone.
   - **Branding**: colors, fonts, logo (favicons are generated from it), mascot/TV images, display text.
   - **Stand setup**: rename/add stands and their URL slugs.
   - **Catalog / Pricing / Menus**: your items, prices, and preset menus.
   - **Users**: add volunteers and give them Manager and/or Pricing (System Admins can also grant Administrator).
   - **QR codes**: print a sign for each stand.

## 8. Backups and restore

- A backup of `data/` (JSON + images) is written daily at 3:00 AM (instance time zone) to
  `/var/lib/hsconcessions/<instance>/backups/<instance>-backup-YYYY-MM-DD.zip` and kept 30 days.
- System Admins can download backups, take one on demand, and **restore** from **Admin → Backups**
  (double confirmation; a pre-restore backup is always taken first; restores are audited).
- To restore by hand (e.g. onto a new server):
  ```bash
  sudo systemctl stop hsconcessions@wildcatsnacks
  cd /var/lib/hsconcessions/wildcatsnacks
  sudo -u hsconcessions mkdir -p data && sudo -u hsconcessions unzip -o /path/to/wildcatsnacks-backup-2026-10-14.zip -d data
  sudo systemctl start hsconcessions@wildcatsnacks
  ```
  (JSON files go in `data/`, the zip's `images/` folder becomes `data/images/`.)
- Copy backups off the server occasionally (e.g. `rsync` to another machine); they are small.

## 9. Updating the app

Publish again (step 2), copy over `/opt/hsconcessions/<instance>/`, and `sudo systemctl restart hsconcessions@<instance>`.
Data is untouched. TV displays pick up new code at their 4:00 AM refresh (or reload them manually); phones pick it up on
the next page load.

## 10. Logs

- App logs: `logs/<instance>-app-YYYY-MM-DD.log`; audit trail: `logs/<instance>-audit-YYYY-MM-DD.jsonl`.
- Both are kept 90 days and can be downloaded from **Admin → Logs** (single files or a date range as ZIP).
