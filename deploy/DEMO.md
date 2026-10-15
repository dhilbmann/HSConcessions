# Public demo ("try it" site)

The demo is the normal app running in the **Demo** environment (`ASPNETCORE_ENVIRONMENT=Demo`, settings in
`src/HSConcessions.Web/appsettings.Demo.json`):

- The sign-in page shows one-click buttons for System Admin, Administrator, Pricing, Manager, and Pricing + Manager.
  No Google/Microsoft setup is needed (those providers are turned off in Demo).
- Every admin page shows a "Demo site" banner.
- All data and images reset to the seed profile every 24 hours (`Demo__ResetIntervalMinutes`), and on every restart of a
  host without a persistent disk.
- Because anyone can sign in as System Admin, log downloads and backups are turned off, and visitor IP addresses are
  not written to the audit log.
- Never point the demo at real data, and never use Demo mode for the real site.

## Quickest path: Render (free, no credit card, temporary `*.onrender.com` name)

1. Push this repository to GitHub (a private repo is fine).
2. Sign in at <https://render.com> with GitHub → **New** → **Blueprint** → pick the repo.
   Render reads [`render.yaml`](../render.yaml) and creates the `wildcatsnacks-demo` web service.
   (Or **New → Web Service**, choose **Docker**, Dockerfile path `deploy/Dockerfile`, and add the environment variables
   `ASPNETCORE_ENVIRONMENT=Demo` and `Seed__Profile=wildcatsnacks`.)
3. Wait for the first build (several minutes). The site is at `https://wildcatsnacks-demo.onrender.com` (or the name Render
   assigns). Share `/admin` for the admin side, `/` for the phone menu, and `/display/stadium` for a TV.

Free-plan notes: the service sleeps after about 15 minutes without visitors, and the first visit afterwards takes
30–60 seconds to wake it. The free plan has no persistent disk, so waking up starts from a fresh copy of the sample
menu — fine for a demo.

## Other hosts

Anything that runs a Docker container behind HTTPS works the same way: build `deploy/Dockerfile`, set
`ASPNETCORE_ENVIRONMENT=Demo` and `Seed__Profile=wildcatsnacks`. The app listens on `$PORT` if the platform sets it,
otherwise 8080, and `Hosting__TrustForwardedHeaders` is already on in Demo so the platform's HTTPS is detected.
