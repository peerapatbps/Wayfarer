# 00 Executive Summary

## System Overview

### Confirmed by code
- The three repositories form a small operational data platform around water-utility monitoring and maintenance support.
- `Uroboros` is a Windows .NET engine with an internal scheduler, an `HttpListener` control/data API on `http://+:8888/`, SQLite-backed runtime/task configuration, Aquadat data processing, lab import, and an explicit Wayfarer integration bridge.
- `BellBeast` is an ASP.NET Core Razor Pages web application listening on port `5082`. It serves the MHxView dashboard, user/admin authentication, local SQLite-backed lookup pages, and many proxy endpoints that relay browser requests to `Uroboros` at `http://localhost:8888`.
- `Wayfarer` is a .NET worker-plus-Playwright collector. It logs into WebPM2 via browser automation, captures access tokens and cookies, calls WebPM2 APIs, and stores work-order snapshots and detailed payload data in local SQLite.

### Recommended interpretation
- The implemented architecture is a practical service chain:
  `External sources -> Uroboros acquisition/processing -> BellBeast dashboard`
  and
  `WebPM2 -> Wayfarer collector -> BellBeast/ Uroboros query surfaces`.
- This is suitable for academic writing as an incremental internal platform rather than a greenfield monolith.

## Repository Roles

| Repository | Current role | Evidence-based maturity |
| --- | --- | --- |
| Uroboros | Data engine, scheduler, local HTTP backend, Aquadat processor, task orchestration hub | Production-oriented internal engine with many implemented tasks |
| BellBeast | Web UI, MHxView dashboard, admin controls, backend proxy, Wayfarer map/list UI | Operational web application with implemented pages and APIs |
| Wayfarer | WebPM2 acquisition worker using Playwright and SQLite snapshot store | Working collector foundation with implemented login, token capture, API fetch, and storage |

## Relationship Between Repositories

### Uroboros <-> BellBeast
- Confirmed by code: BellBeast reads backend routing from `App_Data/backend-config.json`, where `backendBaseUrl` points to `http://localhost:8888`.
- Confirmed by code: BellBeast proxies `/api/process`, `/api/dailyreport`, `/api/chem_report`, `/api/dps/summary`, `/api/tps/summary`, `/api/rws/summary`, `/api/chem/summary`, `/api/event/summary`, `/api/lab/summary`, `/api/cldetector/summary`, and admin task commands to Uroboros.

### Uroboros <-> Wayfarer
- Confirmed by code: Uroboros loads a `Wayfarer` section from `appsettings.json`, registers task `wayfarer.pm.collect`, seeds a `scheduler_task` row, can launch the Wayfarer worker process, and exposes Wayfarer-related data/query/export handling through `WayfarerIntegration.cs`.

### BellBeast <-> Wayfarer
- Confirmed by code: BellBeast reads `App_Data/wayfarer.db` and `App_Data/wayfarer_meta.db`, exposes `/api/wayfarer/*` routes, map-summary routes, and includes a `WebPM` page plus `wayfarer.js`/`wayfarer.css` assets.

## Mapping to Target Reports

### OPA: MHxView Dashboard
- Strongly supported by `BellBeast`.
- Supporting evidence also exists in `Uroboros` because BellBeast dashboard summaries depend on Uroboros summary endpoints and task orchestration.

### LL EP1: Aquadat as a Service
- Strongly supported by `Uroboros`, especially `AquadatFast.cs`, `AquadatRemarkHelper.cs`, `Listener_AQ.cs`, and related SQLite/export logic.
- BellBeast supports report/query front-end and user token capture via login flow.

### LL EP2: WebPM2 as a Service
- Strongly supported by `Wayfarer`.
- BellBeast provides downstream analytical/query presentation.
- Uroboros provides scheduling and optional process orchestration for Wayfarer collection.

## Important Limitations

### Confirmed by code
- BellBeast user login contains a brute-force fallback pattern against Aquadat credentials in `Pages/Login.cshtml.cs`; this is a security concern, not a best-practice pattern.
- Wayfarer writes the discovered working password back into `appsettings.json` in `PlaywrightPmCollector.SaveWorkingPasswordToAppSettingsAsync`; this is implemented but security-sensitive.
- Uroboros and BellBeast contain hardcoded/localhost/internal-network assumptions.
- Wayfarer is implemented as a one-shot worker process, not a continuously running distributed service.
- No evidence of containerization, message broker integration, or centralized observability stack was confirmed from code.

### Not confirmed from code
- Formal deployment automation pipeline.
- Role-based multi-tenant security model.
- Horizontal scaling or high-availability topology.

## Suggested Academic Narrative

1. Present the system as a staged modernization path rather than a single redesign.
2. For OPA, emphasize how a browser dashboard (`BellBeast`) centralizes visibility over operational data previously suited to legacy or fragmented interfaces, while `Uroboros` standardizes backend collection and summary APIs.
3. For LL1, frame Aquadat handling in `Uroboros` as a reusable data service pattern: authenticate, craft payload/config mapping, fetch, normalize, store in SQLite, and export to CSV/report consumers.
4. For LL2, frame Wayfarer as a serviceized acquisition layer for WebPM2: automated browser session establishment, token capture, API harvesting, local snapshot database, and downstream analytics/dashboard consumption.

## Build Validation

- Uroboros: `dotnet build` succeeded with 0 warnings and 0 errors on 2026-05-01.
- BellBeast: `dotnet build` succeeded with 2 warnings and 0 errors on 2026-05-01.
- Wayfarer: `dotnet build` succeeded with 0 warnings and 0 errors on 2026-05-01.
