# HSConcessions

A reusable concession-stand menu system for high schools: TV menu boards, a phone menu reached by QR code, and a
mobile-first management site for volunteers. Each school runs its own branded instance; all school-specific values
(name, colors, fonts, logo, stands, text) come from configuration, not code.

- **TV display** — `/display/{stand}`: 1920×1080, rotates one category at a time, auto-fits every category on one screen,
  red rubber-stamp **SOLD OUT**, "Going Fast!" badges, closed/opening-soon screens. Polls JSON; never reloads while open.
- **Phone menu** — `/` and `/{stand}`: open stands as tabs, grouped by category and sub-category, prices, options,
  upcoming games when nothing is open.
- **Admin** — `/admin`: open/close stands, set times, apply preset menus, toggle selling / Low / Sold Out with one tap,
  catalog with photos, per-stand pricing grid, preset menus, users and roles, permission matrix, calendar, audit log,
  logs, settings, branding editor with live preview, QR codes and printable signs, backups.

No database (JSON files on disk, atomic writes), no payment integration, no third-party requests at runtime.

## Tech

.NET 10 / ASP.NET Core (Razor Pages for admin, static HTML + vanilla JS for public and TV), cookie auth +
OpenID Connect (Google, Microsoft), Serilog, SkiaSharp (image resize/WebP), QRCoder, xUnit.

```
src/HSConcessions.Web/      the app (Api/, Auth/, Pages/Admin/, Services/, Models/, wwwroot/)
tests/HSConcessions.Tests/  pricing, permissions, stand transitions, JSON store atomicity, images, retention, iCal
samples/generic/            neutral seed profile
samples/wildcatsnacks/      Mountain View High School (WildcatSnacks) seed profile
Dockerfile                  container build (public demo; optional for production)
deploy/                     Caddyfile, systemd unit, DEPLOY.md, DEMO.md
```

## Run locally

Requires the .NET 10 SDK.

```bash
cd src/HSConcessions.Web
ASPNETCORE_ENVIRONMENT=Development dotnet run --urls http://localhost:5080
# PowerShell: $env:ASPNETCORE_ENVIRONMENT="Development"; dotnet run --urls http://localhost:5080
```

On first run the empty `data/` folder is seeded from `samples/<Seed:Profile>` (default `generic`; set
`Seed__Profile=wildcatsnacks` to start with the WildcatSnacks instance). Delete `src/HSConcessions.Web/data` to re-seed.

- Phone menu: <http://localhost:5080/> · TV: <http://localhost:5080/display/football> · Admin: <http://localhost:5080/admin>
- **Development sign-in:** without OAuth credentials, the login page shows a *Dev sign in* box (Development
  environment only — it is never registered in Production). Use `admin@localhost.test` (the System Admin in
  `appsettings.Development.json`), or any email you add on the Users page. The allowlist still applies.
- Real Google/Microsoft sign-in: set the client ID/secret (see [deploy/DEPLOY.md](deploy/DEPLOY.md) §5), e.g. in a
  `.env` file in `src/HSConcessions.Web/` (git-ignored):
  `Auth__Providers__Google__ClientId=…`, `Auth__Providers__Google__ClientSecret=…`

## Tests

```bash
dotnet test
```

## Deploy

See [deploy/DEPLOY.md](deploy/DEPLOY.md): self-contained linux-x64/arm64 build, systemd, Caddy (automatic HTTPS),
DNS, OAuth secrets, new-school setup, and backup/restore.

## Configuration reference

| Setting | Where | Default |
|---|---|---|
| `Instance:Name` | appsettings / env | `hsconcessions` (used in cookie, log and backup names) |
| `Seed:Profile` | appsettings / env | `generic` |
| `Storage:DataPath`, `ImagesPath`, `LogsPath`, `BackupsPath` | appsettings / env | `./data`, `./data/images`, `./logs`, `./backups` |
| `Auth:SystemAdmins` | appsettings / env | — (required) |
| `Auth:Providers:{Google,Microsoft}:{Enabled,ClientId,ClientSecret}` | env / `.env` | enabled, no credentials |
| `Features:Events`, `Features:OptionPricing`, `Features:StandScopedRoles` | appsettings / env | `false` |
| Site identity, branding, text, timing, calendar | Admin → Settings / Branding (`data/settings.json`) | generic values |
