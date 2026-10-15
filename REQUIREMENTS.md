# HSConcessions — Requirements

**Project:** HSConcessions — a reusable concession stand menu display and management system for high schools
**Repository:** HSConcessions (school-neutral; each school deploys its own branded instance)
**First instance:** WildcatSnacks — Mountain View High School Wildcats, wildcatsnacks.com
**Owner / System Admin (first instance):** David
**Document date:** 2026-10-14

---

## 1. Purpose

HSConcessions is a reusable concession menu system for high schools. Each school runs its own instance with its own name, branding, domain, and stands. The first instance, **WildcatSnacks** (wildcatsnacks.com), serves Mountain View High School, which runs three concession stands: **Football**, **Baseball**, and **Inside**. This system:

1. Shows what is for sale, with prices, on TVs mounted near each stand.
2. Gives fans a phone-friendly menu (reached by QR code) listing every open stand, what it sells, and the prices.
3. Gives authorized volunteers a phone-friendly management site. They use it to open and close stands, set what is being sold, flag items Low or Sold Out, manage items and prices, and manage users.

Payments are handled separately by Square. **This system does not integrate with Square or any payment system.**

Build the whole system in one pass. There is no MVP or phasing.

**Reusability rule:** no school-specific value may be hardcoded in code, markup, CSS, or images that ship with the app. That covers school name, mascot, site title, colors, fonts, logos, icons, domain, stand names, and display messages. All of it comes from instance configuration (§13–§14). The WildcatSnacks values ship only as a sample instance in `samples/wildcatsnacks/`.

---

## 2. Guiding constraints

| Constraint | Requirement |
|---|---|
| Cost | Must run on the cheapest practical hosting: one small Linux VM or VPS, no database server, no paid services required. |
| Weight | Public and TV pages must be lightweight. They use vanilla HTML, CSS, and JS with no front-end framework. |
| Storage | All data lives in **JSON files on disk**. No database. |
| Freshness | Menu, price, and status data must **never be cached** by the browser, proxies, or the server. Images **should** be cached. |
| Logs | Application and audit logs are written to a `logs/` folder, kept 90 days, and downloadable by admins. |
| Currency | All prices are USD, shown as `$2.50`. |
| Time zone | All times are stored as UTC ISO-8601 and displayed in **America/New_York**. |
| Concurrency | **Last write wins.** Writes must be atomic so a file is never left half-written. |

---

## 3. Technology stack

- **Runtime:** .NET 10 (LTS), ASP.NET Core, built as one self-contained web app.
- **Admin UI:** Razor Pages, server-rendered and mobile-first. Use minimal vanilla JS for toggles (fetch + small API calls) so a tap doesn't reload the page.
- **Public site and TV display:** static HTML, CSS, and vanilla JS served by the same app. They fetch JSON from the API.
- **Authentication:** ASP.NET Core cookie authentication plus OpenID Connect / OAuth for **Google** and **Microsoft**.
- **Logging:** Serilog with daily rolling files.
- **Images:** SkiaSharp (MIT licensed) for resizing and WebP encoding.
- **QR codes:** QRCoder (MIT licensed).
- **Tests:** xUnit project covering pricing resolution, the permission union, stand state transitions, and JSON store atomicity.

### Suggested solution layout
```
HSConcessions.sln
src/HSConcessions.Web/
  Program.cs
  appsettings.json
  Api/                  # minimal API endpoints (public + admin)
  Pages/Admin/          # Razor Pages for management
  Services/             # JsonStore, PricingService, PermissionService, AuditService, ImageService, CalendarProviders, BackupService, RetentionService
  Models/
  wwwroot/
    index.html          # public site (also serves /football, /baseball, /inside)
    display.html        # TV display (/display/{stand})
    css/ js/ img/
tests/HSConcessions.Tests/
samples/
  generic/              # neutral default instance (seed data, placeholder logo)
  wildcatsnacks/        # Mountain View HS instance: settings.json, logo, seed catalog
deploy/
  Caddyfile
  hsconcessions.service # systemd unit (instance name/paths are parameters)
  Dockerfile            # optional
  DEPLOY.md
```

### Configurable paths (appsettings.json)
- `Storage:DataPath` (default `./data`)
- `Storage:ImagesPath` (default `./data/images`)
- `Storage:LogsPath` (default `./logs`)
- `Storage:BackupsPath` (default `./backups`)

---

## 4. Hosting and deployment

- Target **any small Linux VM**, such as a free-tier cloud VM or a low-cost VPS. The app runs as a systemd service behind **Caddy**, which provides free automatic HTTPS via Let's Encrypt.
- Do **not** depend on platform features that would lock the app to one host.
- Avoid hosts with ephemeral file systems. The JSON data, images, logs, and backups must persist across restarts and redeploys.
- Provide `deploy/DEPLOY.md` covering:
  - publishing a self-contained linux-x64 or linux-arm64 build
  - the systemd unit
  - the Caddyfile
  - pointing DNS for the instance's domain (e.g., wildcatsnacks.com)
  - setting up a new school's instance: choose a seed profile, set the System Admin email(s), then set branding in the admin site
  - setting OAuth secrets via environment variables
  - restoring from a backup
- Assume a single app instance. In-process file locking is sufficient.

---

## 5. Roles and permissions

### 5.1 Roles
| Role | Summary |
|---|---|
| **System Admin** | Has every permission, always. Defined by email address(es) in `appsettings.json` (`Auth:SystemAdmins`), not in the JSON data, so it can't be locked out. Only a System Admin can grant or revoke the Administrator role. |
| **Administrator** | Adds and removes users. Assigns Pricing and/or Manager roles. Views and downloads audit and app logs. |
| **Pricing** | Manages the catalog (items, categories, sub-categories, options, pictures, descriptions), prices, and preset menus. |
| **Manager** | Runs stands on game day: opens and closes stands, sets what is selling, marks items Low or Sold Out. |

- A user may hold **any combination** of Administrator, Pricing, and Manager.
- **Permissions are a union.** If any of a user's roles allows a function, the user can perform it. Roles never deny.

### 5.2 Configurable permission matrix
An admin page shows a grid:
- **Rows:** functions (listed below)
- **Columns:** Administrator, Pricing, Manager
- **Cells:** checkboxes

System Admin is not a column because it always has everything. Checking or unchecking a box saves immediately and is audited. The matrix is stored in `data/permissions.json`.

Lockout protection: the rows **Manage users** and **Edit permission matrix** are locked and always checked for Administrator. Only a System Admin can edit the matrix by default; this is itself controlled by the "Edit permission matrix" row, which only the System Admin can change.

| Function | Admin | Pricing | Manager |
|---|:-:|:-:|:-:|
| View admin dashboard | ✔ | ✔ | ✔ |
| Open / close a stand | | | ✔ |
| Set stand opening / closing time | | | ✔ |
| Set which items are selling at a stand | | | ✔ |
| Mark items Low / Sold Out | | | ✔ |
| Apply a preset menu to a stand | | ✔ | |
| Create / edit / delete preset menus | | ✔ | |
| Add / edit categories, sub-categories, items, options | | ✔ | |
| Upload / replace item pictures | | ✔ | |
| Archive / restore catalog entries | | ✔ | |
| Set prices (default and per-stand) | | ✔ | |
| Manage events (only when Events are enabled) | | ✔ | |
| Manage game calendar | ✔ | | |
| Manage users (add/remove, assign Pricing/Manager) | ✔ 🔒 | | |
| View audit log | ✔ | | |
| Download audit and application logs | ✔ | | |
| Edit site settings and generate QR codes | ✔ | | |
| Download / restore backups | | | |
| Edit permission matrix | 🔒 System Admin only by default | | |

The table above shows the **default** matrix. The seed `permissions.json` must match it.

### 5.3 Future: stand-scoped managers
For now, Managers can control **all** stands. The data model must already support scoping: each role assignment has an optional `standIds` array, where empty or missing means all stands. A setting `Features:StandScopedRoles` (default `false`) turns enforcement on later. The user-edit UI shows stand checkboxes only when this setting is enabled.

---

## 6. Authentication

- Sign-in is via **Google** and **Microsoft** (personal and work/school accounts, using the `common` endpoint). No other providers (no Apple).
  - Each provider can be enabled or disabled independently in config. A disabled provider's button is hidden.
  - Structure the provider setup so another OIDC provider could be added later with config and a small registration change, without touching the allowlist or role logic.
- The system stores **only the email address**, plus a display name if the provider supplies one. No passwords are stored.
- **Allowlist only:** after OAuth, the verified email (case-insensitive) must exist in `users.json` with `active: true`, or match a System Admin email. Otherwise show "Your account is not authorized. Contact a concessions administrator." and log the attempt.
- The auth cookie is HTTP-only, Secure, and SameSite=Lax, with a sliding expiration (default 14 days) so volunteers aren't re-prompted every game.
- All admin pages and admin APIs require authentication and a permission check. Use anti-forgery tokens on all state-changing requests.
- Successful logins, failed logins, and logouts are audited.

---

## 7. Data model (JSON files)

All files live in `Storage:DataPath`. Every file has a top-level `"version"` (schema version) and an `"updatedAt"` field.

### 7.1 Write rules
- **Atomic writes:** write to `file.json.tmp`, flush, then rename over `file.json`.
- **Per-file lock:** use an in-process `SemaphoreSlim` per file around each read-modify-write cycle.
- **Last write wins.** There is no optimistic-concurrency rejection.
- **Fresh reads:** files are re-read on each request, or cached in memory with a file-timestamp check. Either way, a change must be visible on the very next request.

### 7.2 Files

| File | Contents |
|---|---|
| `settings.json` | Site settings (§13) |
| `stands.json` | Stand definitions and live state |
| `catalog.json` | Categories, sub-categories, items, options |
| `prices.json` | Default and per-stand prices at every level |
| `menus.json` | Preset menus |
| `users.json` | Allowlisted users and role assignments |
| `permissions.json` | Permission matrix |
| `events.json` | Optional events (feature-flagged off) |
| `calendar.json` | Manually entered upcoming games |

### 7.3 Catalog hierarchy

```
Category            (Beverages, Food, Candy, Snacks)
 └─ Sub-Category    (optional; e.g. Soda, Gatorade, Chips, Energy Drink)
     └─ Item        (e.g. Dr Pepper, Diet Dr Pepper, Pepsi; Doritos, Lays Original)
Category
 └─ Item            (items may sit directly under a category; e.g. Water, Coffee, Hot Chocolate, Pretzel)
```

- **Varieties are Items.** "Soda" is a sub-category, and "Diet Dr Pepper" is an item in it. Each item can be marked Sold Out, Low, or selling on its own, and can carry its own price.
- **Options** belong to an item and do not create separate inventory. Examples:
  - Pretzel: Salt / Cheese / No Salt
  - Nachos: Cheese / Extra Cheese / Jalapeños
- Each option has an optional `priceDelta` (default 0). Price deltas are shown **only** when `Features:OptionPricing` is `true` (default `false`).
- Any category, sub-category, item, or option can be **archived**. Archived entries are hidden everywhere except the Archive view and can be restored. Nothing is hard-deleted.

**catalog.json (example)**
```json
{
  "version": 1,
  "updatedAt": "2026-10-13T23:00:00Z",
  "categories": [
    { "id": "cat-beverages", "name": "Beverages", "sortOrder": 1, "imageId": null, "archived": false }
  ],
  "subCategories": [
    { "id": "sub-soda", "categoryId": "cat-beverages", "name": "Soda", "description": "20 oz bottles",
      "sortOrder": 1, "imageId": "img-3f9a...", "archived": false }
  ],
  "items": [
    { "id": "itm-drpepper", "categoryId": "cat-beverages", "subCategoryId": "sub-soda",
      "name": "Dr Pepper", "description": "", "imageId": "img-77c1...", "sortOrder": 1, "archived": false,
      "options": [] },
    { "id": "itm-nachos", "categoryId": "cat-food", "subCategoryId": null,
      "name": "Nachos", "description": "Tortilla chips with warm cheese", "imageId": "img-a12b...",
      "sortOrder": 3, "archived": false,
      "options": [
        { "id": "opt-cheese", "name": "Cheese", "priceDelta": 0, "archived": false },
        { "id": "opt-xcheese", "name": "Extra Cheese", "priceDelta": 0.50, "archived": false },
        { "id": "opt-jalapeno", "name": "Jalapeños", "priceDelta": 0, "archived": false }
      ] }
  ]
}
```

### 7.4 Pricing

A price can be set at the **Category**, **Sub-Category**, or **Item** level. At each level there is a **default** price and optional **per-stand** overrides.

**prices.json (example)**
```json
{
  "version": 1,
  "updatedAt": "...",
  "prices": [
    { "level": "subCategory", "targetId": "sub-soda", "standId": null,       "amount": 2.50 },
    { "level": "subCategory", "targetId": "sub-soda", "standId": "baseball", "amount": 3.00 },
    { "level": "item",        "targetId": "itm-chickfila", "standId": null,  "amount": 7.00 }
  ]
}
```

**Resolution order.** The first match wins. For item *I* at stand *S*:
1. Item price for stand S
2. Item default price
3. Sub-Category price for stand S
4. Sub-Category default price
5. Category price for stand S
6. Category default price
7. No price. The item is shown with no price on public pages, and the admin UI shows a warning.

Requirements:
- Implement this in a single `PricingService.Resolve(itemId, standId)` with unit tests.
- Prices persist until changed. There is no per-event pricing.
- The admin pricing screen shows each item's **effective price per stand** and **where it came from** (e.g., "$3.00 — Soda @ Baseball").

### 7.5 Stands (definition and live state)

**stands.json (example)**
```json
{
  "version": 1,
  "updatedAt": "...",
  "stands": [
    {
      "id": "football", "name": "Football", "slug": "football", "sortOrder": 1,
      "state": {
        "isOpen": false,
        "openingAt": "2026-10-16T22:00:00Z",
        "closingAt": "2026-10-17T02:00:00Z",
        "lastAppliedMenuId": "menu-varsity-football",
        "items": {
          "itm-drpepper": { "selling": true, "status": "ok" },
          "itm-nachos":   { "selling": true, "status": "low" }
        }
      }
    }
  ]
}
```
- `status` is one of `ok`, `low`, or `soldOut`.
- An item is **on the menu at a stand** only if `selling: true` and the item is not archived.
- Stands are data, not code. Users with "Edit site settings" can add, rename, reorder, and archive stands and set each stand's **slug**, which is used in `/{slug}` and `/display/{slug}`.
  - Slugs must be unique, lowercase, and URL-safe.
  - Slugs must not collide with reserved routes: `admin`, `api`, `display`, `images`, `css`, `js`, `img`, `assets`, `theme.css`, `manifest.json`.
  - The public site and admin must handle any number of stands. On phones, tabs scroll horizontally when they don't fit.

### 7.6 Preset menus

**menus.json (example)**
```json
{
  "version": 1,
  "updatedAt": "...",
  "menus": [
    { "id": "menu-varsity-football", "name": "Varsity Football", "standId": "football",
      "itemIds": ["itm-drpepper", "itm-chickfila", "itm-nachos", "..."], "archived": false },
    { "id": "menu-jv-football", "name": "JV Football", "standId": "football",
      "itemIds": ["..."], "archived": false }
  ]
}
```

- **Applying a menu** to a stand sets `selling: true` for exactly the menu's items and `selling: false` for every other item at that stand. It resets Low/Sold Out to `ok` and records `lastAppliedMenuId`.
- After applying, any individual item can still be toggled on or off. The preset is only a starting point.
- The menu editor lets users pick items by checking whole categories or sub-categories (a bulk select) or individual items.
- A menu can be duplicated, e.g., "Varsity Football" → "JV Football".

### 7.7 Events (optional, disabled)

- `Features:Events` defaults to `false`. When off, events are hidden from all UI and have no effect.
- When on, an event has: `id`, `name`, `standIds`, `startsAt`, `menuId` (optional), `notes`. The only behavior is a **"Prepare stand for event"** button that applies the event's menu and sets the stand's opening time from `startsAt`.
- Keep this minimal. It exists so it can be turned on later without a schema change.

### 7.8 Users

**users.json (example)**
```json
{
  "version": 1,
  "updatedAt": "...",
  "users": [
    { "email": "volunteer@example.com", "displayName": "Pat", "active": true,
      "roles": [
        { "role": "Manager", "standIds": [] },
        { "role": "Pricing", "standIds": [] }
      ],
      "createdAt": "...", "createdBy": "david@example.com", "lastLoginAt": "..." }
  ]
}
```

- Removing a user sets `active: false`. The record is kept for audit history and can be re-activated.

### 7.9 Calendar

Calendar data is behind an `ICalendarProvider` interface so new sources can be added.

- **ManualCalendarProvider** (default) reads `calendar.json`. Admins add, edit, and remove entries with: date/time, opponent/title, team level (Varsity/JV/etc.), sport, home/away, location.
- **ICalFeedCalendarProvider** fetches an iCal URL from settings, caches it server-side for 30 minutes, and maps events to the same shape.
- `settings.calendar.provider` selects the active provider.

---

## 8. Stand lifecycle

| State | How it happens | TV display | Public site |
|---|---|---|---|
| **Closed**, no opening time | Default; also after closing | Logo + "Concessions Closed" | Stand not shown |
| **Closed**, opening time more than 60 min away | Manager sets opening time | Logo + "Concessions Closed" | Stand not shown |
| **Closed**, opening time within 60 min | Time window | Logo + "Concessions Opening at 6:00 PM" | Stand not shown; a notice "Football stand opens at 6:00 PM" appears above the calendar |
| **Closed**, opening time has passed | Manager hasn't opened yet | Logo + "Opening Soon" | Notice "Football stand opening soon" |
| **Open** | Manager taps **Open** | Menu rotation | Stand tab shown |

Rules:
- **Opening and closing are manual only.** Times are informational and drive the messages above; they never change state automatically.
- The 60-minute window is configurable (`settings.openingSoonMinutes`, default 60).
- When open and a closing time is set, the public page shows "Open until 9:00 PM".
- **Closing resets everything for that stand:**
  - all items `selling: false` and `status: ok`
  - `openingAt` and `closingAt` cleared
  - `lastAppliedMenuId` kept so "Re-apply last menu" is one tap next time
- Closing requires a confirmation tap ("Close Football stand? This resets all items.").
- Every state change is audited.

---

## 9. Public site (instance root, e.g., wildcatsnacks.com)

### 9.1 Routes
| Route | Behavior |
|---|---|
| `/` | Menu for all open stands |
| `/{standSlug}` (e.g., `/football`) | Same page with that stand's tab pre-selected. If that stand is closed, show "The Football stand is closed" plus whatever else is open, or the calendar. Unknown slugs return a friendly 404 that links to `/`. |

Stand slugs are case-insensitive.

### 9.2 Layout and behavior
- **Header:** the instance logo and site title, in the instance colors.
- **No stands open:**
  - message "No concession stands are open right now"
  - any opening-soon notices (§8)
  - the **upcoming games** list from the calendar provider (next 5 games, with date, time, opponent, level, and home/away)
- **Exactly one stand open:** show its menu directly with the banner **"Concessions are sold at the Football stand."** Do not show tabs.
- **Two or three stands open:** show one tab per open stand, in stand sort order. The selected tab is reflected in the URL (`/baseball`) with `history.replaceState`, without a page reload.
- **Menu content:**
  - grouped by category, in category sort order
  - within a category, sub-categories are shown as a group with their varieties listed
  - each entry shows picture, name, description, price, and options (e.g., "Options: Salt · Cheese · No Salt")
  - when a sub-category's varieties share one price, the price shows once on the group header ("Soda — $2.50") and the varieties are listed beneath; varieties with a different price show it inline
- **Sold Out items are hidden** on the public site. A sub-category or category with nothing left to show is hidden.
- **Low items** show a small orange **"Going Fast!"** badge.
- The page scrolls vertically and works well on phones first, then tablets and desktops.
- Data refreshes on load, every 2 minutes, and when the browser tab becomes visible again. Updates go into the DOM without a full reload.

### 9.3 Footer
Footer text from settings (default "Payment accepted at the stand."). Optional: a link to the admin login.

---

## 10. TV display (/display/{stand})

### 10.1 Target
- **1920×1080 landscape**, full screen, no scrollbars, no cursor (`cursor: none`), no interaction needed.
- **Likely running in a smart TV's built-in browser.** Write conservative JS (ES2017, no build step, no modules required), use CSS Grid/Flexbox with no bleeding-edge CSS, and test in an older Chromium build.
- Text must be readable from about 20 feet: item names at least 40px, prices at least 48px at 1080p.

### 10.2 Category rotation
- When open, show **one category at a time** for `settings.display.categorySeconds` seconds (default 12), then crossfade (~600 ms) to the next category that has selling items. Loop forever.
- **Never split a category across screens.** The layout auto-sizes the grid (columns, rows, image size, font size) so the whole category fits.
  - If it still doesn't fit at minimum readable sizes, drop the item images for that category before shrinking text further.
- Each screen shows:
  - the category name as a large heading
  - the item grid
  - a footer with the stand name
  - small page dots showing which category of how many is on screen
- If only one category has selling items, show it with no rotation.

### 10.3 Item states on TV
- **OK:** normal.
- **Low:** orange "Going Fast!" badge.
- **Sold Out:** the item **stays visible** with a **red rubber-stamp "SOLD OUT"** over its name.
  - Styled like inked rubber: rotated about −12°, in the configured stamp color (default red `#C8102E`), showing the configured stamp text (default "SOLD OUT") in uppercase heavy condensed type, with a double-rule border with rounded corners and slight transparency (opacity ~0.85).
  - A distressed/grainy ink texture via an inline SVG `feTurbulence` + `feDisplacementMap` filter, or an SVG mask.
  - No image files required.
  - The item's picture and price are dimmed (opacity ~0.4).
- Not selling: not shown.

### 10.4 Closed states
Follow §8: the instance's closed-screen image centered and large (the mascot image if set, otherwise the logo), with the message beneath. Message wording comes from settings; defaults:
- "Concessions Closed"
- "Concessions Opening at 6:00 PM"
- "Opening Soon"

The background uses the instance colors, or the optional TV background image.

### 10.5 Refresh
- Poll the stand's display JSON every `settings.display.refreshSeconds` (default **120**). Fetch **JSON only**, never reload the page.
- Apply changes in place: update prices, stamp or unstamp items, add or remove items, and switch between open and closed screens. **No full-page flicker.** Structural changes take effect at the next category transition, not mid-display.
- If a fetch fails, keep showing the last good data and show a tiny unobtrusive indicator in a corner. Retry on the next interval. After 3 consecutive failures, also retry every 30 seconds until success.
- At 4:00 AM local time, do a full `location.reload()` to pick up deployed code changes and clear any memory growth. Never reload while a stand is open.
- **Burn-in mitigation:** shift the layout by a few pixels every few minutes on the closed screen.

---

## 11. Caching rules

**Never cache** any endpoint returning menu, price, stand, or status data:
- Response headers: `Cache-Control: no-store, no-cache, must-revalidate, max-age=0`, `Pragma: no-cache`, `Expires: 0`.
- Client fetches use `{ cache: "no-store" }` **and** append a `?t=<timestamp>` cache-buster.
- `index.html` and `display.html` are also served `no-cache` so code updates are picked up.

**Do cache** images:
- Images are saved with content-hashed filenames (`img-<sha256-prefix>.webp`) and served with `Cache-Control: public, max-age=31536000, immutable`.
- A new upload produces a new filename, so changes show up immediately.

Static CSS/JS may use a version query string tied to the build and cache for one day.

---

## 12. Admin site (/admin)

**Mobile-first.** Volunteers use phones at the stand: large tap targets (minimum 48px), one-hand use, works on a slow cell connection.

### 12.1 Login
`/admin/login` shows a button per enabled OAuth provider.

### 12.2 Dashboard
One card per stand showing:
- open/closed
- opening and closing times
- counts of items selling, Low, and Sold Out
- quick actions, according to the user's permissions

### 12.3 Stand control (Manager)
The main game-day screen for one stand:
- **Open / Close** buttons. Close requires confirmation.
- Set or clear **opening time** and **closing time** (time picker; date defaults to today).
- **Apply preset menu** (dropdown of that stand's menus) and **Re-apply last menu**.
- A list of all catalog items grouped by category and sub-category, each with:
  - a **Selling** toggle
  - a three-state status control: **OK / Low / Sold Out**
- Bulk toggles at the category and sub-category level ("All Soda selling", "All Soda sold out").
- Filters: "Selling only", "Low / Sold Out only".
- A search box.
- Each tap saves immediately through the API, with an optimistic UI update and a small saved/failed indicator. Failures revert the control and show an error.

### 12.4 Catalog (Pricing)
- CRUD for categories, sub-categories, items, and options. Drag or arrow buttons for sort order.
- Item fields: name, description, category, sub-category (optional), picture, options.
- **Picture upload:**
  - accepts JPEG, PNG, and WebP up to 15 MB; uses `accept="image/*"` with `capture` so phones can take a photo directly
  - server auto-orients using EXIF, resizes to max 800px on the long edge, encodes to WebP (quality ~80), and creates a 300px thumbnail
  - EXIF metadata is stripped
- **Archive view:** list archived entries with a **Restore** action.

### 12.5 Pricing (Pricing)
- A grid of items with one column per stand plus a Default column, grouped by category and sub-category.
- Category and sub-category header rows have their own editable price cells.
- Each cell shows the **effective** price. Explicitly set prices are bold; inherited prices are grey with a tooltip or caption showing the source.
- Editing a cell sets a price at that level for that stand (or the default). Clearing it removes the override.
- Items with no resolvable price are flagged in red.

### 12.6 Menus (Pricing)
- List, create, duplicate, rename, and archive preset menus. Each belongs to a stand.
- The editor is a checklist of the catalog with category and sub-category bulk checkboxes.

### 12.7 Users (Administrator)
- Add a user by email. Assign Pricing and/or Manager.
- Administrator can be assigned only by a System Admin.
- Deactivate and reactivate users. Show last login time.

### 12.8 Permissions (System Admin by default)
The permission matrix from §5.2, with checkboxes that save on change.

### 12.9 Calendar (Administrator)
Manage manual games, or set the iCal URL and choose the provider.

### 12.10 Audit log (Administrator)
- Table with filters: date range, user, action type, entity.
- Paged, newest first.
- **Download** the filtered results as CSV.

### 12.11 Logs (Administrator)
List files in `logs/` (both app and audit) with size and date. Download each file, or download a date range as a ZIP.

### 12.12 Settings and QR codes (Administrator)
- Settings editor (§13).
- **Branding editor** (§14) with a live preview of the phone page and the TV display.
- QR code generator: one QR for the **site base URL**, plus one per stand slug.
  - Download as PNG (1024px) and SVG.
  - Each QR has an optional printable sign: a letter-size HTML page with the logo, the "Scan for the menu" caption, the QR, and the URL in text.
  - QR codes are generated from `settings.siteBaseUrl`, so they update when the domain changes.

### 12.13 Backups (System Admin)
List and download backup ZIPs. Restore from a backup with a double confirmation. Restoring is audited and first takes a backup of the current data.

### 12.14 Events (only if `Features:Events`)
Simple event list and editor (§7.7).

---

## 13. Settings (settings.json)

Defaults below are the neutral **generic** values. The WildcatSnacks values appear in the right-hand column and ship as `samples/wildcatsnacks/settings.json`. All of these are editable in the admin site; changes are audited and take effect on the next page load or TV refresh without a restart.

### Identity
| Key | Generic default | WildcatSnacks | Notes |
|---|---|---|---|
| `siteBaseUrl` | `https://example.com` | `https://wildcatsnacks.com` | Used for QR codes, absolute links, OAuth redirect hints |
| `siteTitle` | `HS Concessions` | `Wildcat Snacks` | Browser title, header, PWA name |
| `siteShortName` | `Concessions` | `Wildcat Snacks` | PWA short name, small headers |
| `schoolName` | `Your High School` | `Mountain View High School` | |
| `mascotName` | `Mascots` | `Wildcats` | Available as `{mascot}` in message templates |
| `tagline` | `""` | `""` | Optional line under the header |
| `timeZone` | `America/New_York` | `America/New_York` | Any IANA zone |

Currency is always USD.

### Branding (see §14)
| Key | Generic default | WildcatSnacks |
|---|---|---|
| `colors.primary` | `#1F3A5F` (navy) | `#861F41` (maroon) |
| `colors.accent` | `#F2A900` (gold) | `#E5751F` (burnt orange) |
| `colors.dark` | `#000000` | `#000000` |
| `colors.light` | `#FFFFFF` | `#FFFFFF` |
| `colors.stamp` | `#C8102E` | `#C8102E` |
| `colors.lowBadge` | = accent | = accent |
| `fonts.display` | `Oswald` | `Oswald` |
| `fonts.body` | `Inter` | `Inter` |
| `images.logo` | bundled neutral placeholder | uploaded logo |
| `images.mascot` | none (falls back to logo) | optional wildcat image |
| `images.favicon` | auto-generated from logo | auto-generated from logo |
| `images.tvBackground` | none | none |

### Display text (templates)
| Key | Default |
|---|---|
| `text.closed` | `Concessions Closed` |
| `text.openingAt` | `Concessions Opening at {time}` |
| `text.openingSoon` | `Opening Soon` |
| `text.soldOutStamp` | `SOLD OUT` |
| `text.lowBadge` | `Going Fast!` |
| `text.singleStandBanner` | `Concessions are sold at the {stand} stand.` |
| `text.noStandsOpen` | `No concession stands are open right now.` |
| `text.footer` | `Payment accepted at the stand.` |

Supported template tokens: `{time}`, `{stand}`, `{school}`, `{mascot}`, `{site}`.

### Behavior
| Key | Default | Notes |
|---|---|---|
| `openingSoonMinutes` | 60 | |
| `display.categorySeconds` | 12 | |
| `display.refreshSeconds` | 120 | |
| `public.refreshSeconds` | 120 | |
| `public.upcomingGamesCount` | 5 | |
| `calendar.provider` | `manual` | `manual` or `ical` |
| `calendar.icalUrl` | `null` | |

### Deployment-level (appsettings.json / environment, not editable in the UI)
- `Instance:Name` (e.g., `wildcatsnacks`): used in log file names, backup names, and the cookie name, so two instances on one server don't collide.
- `Seed:Profile`: `generic` (default) or a folder under `samples/`. Used only on first run when `data/` is empty.
- `Auth:SystemAdmins`, OAuth client IDs and secrets, and `Storage:*` paths.

Feature flags live in `appsettings.json` because they're deployment-level: `Features:Events`, `Features:OptionPricing`, `Features:StandScopedRoles`.

---

## 14. Branding and visual design

All visual identity is driven by settings (§13). **No color, font, logo, or school name appears in CSS, markup, or JS source.**

### 14.1 Theming mechanism
- The server generates `/theme.css` from settings. It defines CSS custom properties (`--color-primary`, `--color-accent`, `--color-dark`, `--color-light`, `--color-stamp`, `--color-low`, `--font-display`, `--font-body`) plus derived shades (primary-dark, accent-light) computed server-side.
- All public, TV, and admin CSS uses only these variables.
- `/theme.css` is served with `Cache-Control: no-cache` and an ETag, so branding changes appear on the next load or refresh.
- The TV display re-checks the theme ETag on each refresh and swaps the stylesheet in place, without a page reload.
- `/manifest.json` (PWA name, short name, theme color, icons) and `<title>` / meta tags are generated from settings the same way.

### 14.2 Role of each color
- **TV:** dark background (`dark`, or a gradient toward `primary`), `light` text, `accent` prices, `primary` category header bar.
- **Public:** `light` background, `primary` header, `accent` highlights. Respect `prefers-color-scheme` for dark mode by deriving dark-mode values from the same palette.
- **Sold Out stamp:** `stamp` color. **Low badge:** `lowBadge` color.

### 14.3 Fonts
- Ship a small bundled, self-hosted set of open-licensed fonts in `wwwroot/fonts`: Oswald, Bebas Neue, Anton, and Roboto Condensed for display; Inter, Roboto, and Open Sans for body.
- Settings choose from the bundled list. No third-party font requests at runtime.

### 14.4 Images and icons
- **Logo:** uploaded in the Branding editor. PNG, SVG, or WebP; SVG is sanitized (no scripts or external references).
- **Mascot image:** optional, used on the TV closed screen.
- **TV background:** optional.
- **Favicon and app icons:** auto-generated from the logo at upload (16, 32, 180, 192, and 512 px PNG plus `favicon.ico`), unless a dedicated favicon is uploaded.
- **Category icons:** optional per-category image (`imageId` in the catalog), shown next to category headers when set.

### 14.5 Branding editor (admin)
- Color pickers with hex inputs, font dropdowns, image uploads, and text template fields.
- A **live preview** pane shows a sample phone menu, a sample TV category screen (with a Sold Out stamp and Low badge), and the closed screen, all using unsaved values.
- **Contrast check:** warn (but don't block) when text/background pairs fall below WCAG AA 4.5:1. Example pairs: light on primary, light on dark, accent price on dark.
- A **Reset to defaults** action, and an **Export / Import** action that downloads or uploads the branding block as JSON so a school can share its setup.

### 14.6 WildcatSnacks instance values
Maroon `#861F41` (primary), Burnt Orange `#E5751F` (accent), Black, and White, matching the Virginia Tech palette. Stamp red `#C8102E`. The logo is a placeholder until David supplies the real file. These live only in `samples/wildcatsnacks/`.

---

## 15. API

All JSON. Admin endpoints require authentication, an anti-forgery token, and a permission check, and every write is audited.

### Public (no auth, no-store)
- `GET /api/public/state`: all stands with open/closed and timing state, plus the resolved menus for open stands (items with resolved prices, status, options, image URLs) and upcoming games.
  - Excludes sold-out items (the public site hides them).
  - Never exposes users, emails, audit data, or archived items.
- `GET /api/public/display/{standId}`: the same data for one stand, **including** sold-out items (the TV stamps them), plus `categorySeconds`, `refreshSeconds`, and `serverTime`.
- `GET /images/{file}`: cached images.

### Admin (examples; Claude Code may refine)
- `POST /api/admin/stands/{id}/open`, `/close`, `/times`, `/apply-menu`
- `PATCH /api/admin/stands/{id}/items/{itemId}` with body `{ selling?, status? }`
- `POST /api/admin/stands/{id}/bulk` with body `{ scope: category|subCategory, targetId, selling?, status? }`
- CRUD under `/api/admin/catalog/...`, `/api/admin/prices`, `/api/admin/menus`, `/api/admin/users`, `/api/admin/permissions`, `/api/admin/calendar`, `/api/admin/settings`
- `GET /api/admin/audit?from&to&user&action&entity&page` and `GET /api/admin/audit/export.csv?...`
- `GET /api/admin/logs`, `GET /api/admin/logs/{file}`, `GET /api/admin/logs/zip?from&to`
- `GET /api/admin/qr?target=site|football|baseball|inside&format=png|svg`
- `GET /api/admin/backups`, `GET /api/admin/backups/{file}`, `POST /api/admin/backups/{file}/restore`

---

## 16. Audit trail

Every create, update, archive, restore, state change, price change, permission change, user change, settings change, menu application, login, failed login, logout, backup restore, and log download is audited.

**Record:**
```json
{ "timestamp": "2026-10-16T23:14:05Z", "user": "pat@example.com", "action": "SetItemStatus",
  "entityType": "StandItem", "entityId": "football/itm-nachos", "entityName": "Nachos @ Football",
  "details": { "from": "ok", "to": "soldOut" }, "ip": "203.0.113.5" }
```

- Stored as JSON Lines in `logs/audit-YYYY-MM-DD.jsonl`. Times are UTC in the file and America/New_York in the UI and CSV export.
- Viewable by **System Admin** and **Administrator** (via the "View audit log" permission).

---

## 17. Logging, retention, and backups

- **App logs:** Serilog daily rolling files `logs/app-YYYY-MM-DD.log`, Information level and above, including unhandled exceptions and request summaries for admin endpoints. Never log OAuth tokens or secrets.
- **Retention:** a background `RetentionService` runs daily and deletes app and audit log files older than **90 days**.
- **Backups:** a background `BackupService` runs daily at 3:00 AM local time. It zips `data/` (JSON plus images) to `backups/backup-YYYY-MM-DD.zip` and keeps **30 days**. A backup is also taken before any restore.

---

## 18. Seed data

On first run, if `data/` is empty, copy the seed profile named by `Seed:Profile` into `data/`. Two profiles ship:
- **`samples/generic/`**: neutral branding (§13 generic defaults), a placeholder logo, and the sample catalog below.
- **`samples/wildcatsnacks/`**: the same catalog with Mountain View High School / Wildcats identity and colors.

Both profiles contain:

- **Stands:** Football, Baseball, Inside (all closed).
- **Categories:** Beverages, Food, Candy, Snacks.
- **Sample catalog** (placeholder images or none):

| Category | Sub-category | Items | Options |
|---|---|---|---|
| Beverages | — | Water, Coffee, Hot Chocolate | — |
| Beverages | Soda | Pepsi, Diet Pepsi, Dr Pepper, Diet Dr Pepper, Mountain Dew, Sierra Mist | — |
| Beverages | Gatorade | Fruit Punch, Lemon-Lime, Cool Blue, Orange | — |
| Beverages | Energy Drink | Red Bull, Monster | — |
| Food | — | Hot Dog, Pretzel, Nachos, Chick-fil-A Sandwich | Pretzel: Salt / Cheese / No Salt; Nachos: Cheese / Extra Cheese / Jalapeños |
| Candy | — | Sour Skittles, Skittles, M&M's, Reese's Cups | — |
| Snacks | Chips | Doritos Nacho Cheese, Lay's Original, Lay's Sour Cream & Onion | — |
| Snacks | — | Popcorn, Sunflower Seeds | — |

- **Prices:** sample defaults, plus Soda $2.50 default and $3.00 @ Baseball, to demonstrate overrides.
- **Menus:**
  - "Varsity Football" (Football): includes the Chick-fil-A Sandwich
  - "JV Football" (Football): no Chick-fil-A
  - "Baseball Default" (Baseball)
  - "Inside Default" (Inside)
- **Permissions:** the default matrix from §5.2.
- **Calendar:** two sample upcoming games.
- **Users:** empty. The System Admin comes from appsettings.

---

## 19. Security and privacy

- HTTPS only, with HSTS enabled.
- No personal data beyond volunteer email addresses and display names. No fan or student data is collected. No analytics or third-party trackers.
- Validate uploads: check MIME type and magic bytes, re-encode every image, never serve the original upload.
- Rate-limit the login callback and admin write endpoints (ASP.NET Core rate limiter).
- Secrets (Google and Microsoft OAuth client secrets) come from environment variables or a `.env` file that is not committed. Include `appsettings.Example.json`.

---

## 20. Acceptance criteria

1. With no stands open, `/` shows the closed message and upcoming games. With one open, it shows that stand directly with "Concessions are sold at the X stand." With two or three open, it shows tabs. `/baseball` pre-selects Baseball.
2. Marking an item Sold Out removes it from the public page on the next refresh, and shows it with the red angled rubber stamp on `/display/{stand}` within 2 minutes, with no full-page reload or flicker.
3. The TV rotates categories every N seconds. No category is ever split, and large categories shrink or drop images to fit at 1920×1080.
4. A closed stand's TV shows the logo and "Concessions Closed". 59 minutes before the opening time it shows "Concessions Opening at 6:00 PM". After the opening time, while still closed, it shows "Opening Soon". The stand only opens when a Manager taps Open.
5. Closing a stand resets selling flags, Low/Sold Out, and times. "Re-apply last menu" restores the prior menu in one tap.
6. Price resolution follows §7.4 exactly. Soda shows $2.50 at Football and $3.00 at Baseball. Unit tests cover all seven resolution steps.
7. Applying "Varsity Football" makes the Chick-fil-A Sandwich available at Football. Applying "JV Football" does not. Individual items can be toggled afterward.
8. A user with Manager + Pricing gets the union of both roles' permissions. Unchecking a function in the matrix immediately removes access for users whose roles no longer grant it.
9. An email not in the allowlist cannot reach any admin page after OAuth, and the attempt appears in the logs.
10. Every change appears in the audit log with user, action, entity, before/after, and time. Admins can filter it and download CSV. Logs older than 90 days are deleted automatically.
11. Menu/state API responses carry no-store headers and are never served from cache. Images are served with immutable caching and content-hashed names.
12. JSON writes are atomic. Killing the process mid-write never leaves a corrupt file (covered by a test).
13. Uploaded photos are resized to at most 800px WebP with EXIF stripped.
14. QR codes for the base URL and each stand download as PNG and SVG and reflect `siteBaseUrl`.
15. Archived items disappear from all menus and can be restored with their prior data intact.
16. The app runs as a single self-contained .NET 10 process on a small Linux VM behind Caddy, following `deploy/DEPLOY.md`.
17. A fresh deploy with `Seed:Profile=generic` shows neutral branding. Changing school name, colors, fonts, logo, stand names and slugs, and message text in the admin site updates the phone page and TV display with no code change or restart.
18. A case-insensitive search of `src/` for "Wildcat", "Mountain View", "wildcatsnacks", "861F41", and "E5751F" finds no matches. Those values exist only under `samples/wildcatsnacks/`.
19. Adding a fourth stand in the admin site creates working `/{slug}` and `/display/{slug}` routes, a QR code, and a tab on the public site.

---

## 21. Out of scope

- Payments, Square integration, ordering, carts, or inventory counts
- Stand-scoped manager enforcement (modeled but off; §5.3)
- Per-event pricing
- Native mobile apps
- Multiple schools in one deployment (multi-tenancy). Each school deploys its own instance; two instances can share a server by using different `Instance:Name`, ports, and storage paths.
