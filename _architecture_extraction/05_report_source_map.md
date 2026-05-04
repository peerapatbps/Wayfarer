# 05 Report Source Map

## A. OPA — MHxView Dashboard

### Background / problem
- Confirmed by code: BellBeast centralizes many operational views into one MHxView page with configurable slots and multiple domain blocks.
- Confirmed by code: Uroboros provides normalized summary APIs, suggesting the dashboard is built over a centralized backend instead of isolated point screens.

### Before / after
- Recommended interpretation: before-state likely involved fragmented local/legacy operational interfaces; after-state is BellBeast MHxView backed by Uroboros APIs.
- Not confirmed from code: direct VB.NET/WinForms source code is not present in the inspected repositories.

### Process
- BellBeast `MHxView.cshtml` dynamic slot layout
- BellBeast JS block modules
- Uroboros summary endpoints
- BellBeast backend proxy to `localhost:8888`

### Output / outcome
- Confirmed by code: centralized dashboards for TPS, DPS, RPS, CHEM, LAB, EVENT, PTC, CL detector.
- Confirmed by code: summary/report/export access through web routes.

### Key success factors
- Confirmed by code: local proxy abstraction in BellBeast
- Confirmed by code: reusable summary APIs in Uroboros
- Confirmed by code: configurable slot-based UI composition

### Risks / limitations
- User login brute-force fallback in BellBeast
- Localhost and LAN dependency
- Not confirmed from code: formal role-separation or audit trail

### KM / organization alignment
- Recommended interpretation: the architecture supports knowledge codification by converting scattered operational data into repeatable services and dashboard modules.

## B. LL1 — Aquadat as a Service

### Package diagram
- Strongest evidence: `Uroboros\AquadatFast.cs`, `AquadatRemarkHelper.cs`, `Program.cs`, `BellBeast\Pages\Login.cshtml.cs`

### Data query flow
- Confirmed by code: login/enroll -> token -> fetch by plant/station/config mapping -> parse rows -> pivot/normalize -> export/store.

### Data write / storage flow
- Confirmed by code: writes to `AQ_readings_narrow_v2`, `AQ_readings_FWS_v2`, latest views, and CSV exports.

### Remark / event flow
- Confirmed by code: `AquadatRemarkHelper.cs` exists with bearer-auth HTTP handling and SQLite interactions.
- Not fully confirmed from code: complete external business semantics of each remark/event field.

### Automation pseudo-code
- Strongest evidence: `MultiOutputRequest`, `ProcessMultiAsync`, DB clear/upsert methods.

### Payload structure
- Confirmed by code: request objects include begin/end, token, plant forcing, mapping sources, CSV outputs, DB writes, prefixes, meta lookup, and mode flags.

### IoT use case
- Recommended interpretation: suitable because the pipeline accepts parameter/config mappings and time-series writes into SQLite.

### VBA / Excel use case
- Confirmed by code: CSV export paths and spreadsheet/report handling exist.

### Dashboard / export use case
- Confirmed by code: BellBeast login and Uroboros processing endpoints can support browser-driven export/query flows.

### Risks / limitations
- Hardcoded external URLs
- Sensitive token/credential handling
- SQLite/local-file architecture

## C. LL2 — WebPM2 as a Service

### WebPM2 acquisition flow
- Strongest evidence: `Wayfarer.Playwright\Services\PlaywrightPmCollector.cs`

### Playwright / token / session flow
- Confirmed by code: browser automation, ADFS login, token endpoint interception, cookie header construction.

### Work order list flow
- Confirmed by code: paged API fetch with `pageSize=1000`, offset looping, 365-day cutoff.

### Work order detail flow
- Confirmed by code: per-work-order API fetch and raw JSON retention.

### Enriched data fields
- Confirmed by code: schedule/actual durations, downtime, departments, task history, damage/failure, actual manhours, workflow flags, people/roles.

### Database / snapshot flow
- Confirmed by code: normalized SQLite schema in `SqlitePmSnapshotStore.cs`.

### Analytics potential
- Breakdown frequency: partially supported by status/history/task data.
- Downtime: strongly supported by `dt_duration`.
- OT cost / maintenance cost: partially supported by `actual_manhrs`, `amount`, `unit_cost`.
- Department/unit analysis: supported by department/cost center/site fields.
- Equipment replacement planning: partially supported through equipment, work history, failure data.
- Budget forecasting: partially supported through cost/manhour fields.

### Risks / limitations
- Password rotation stored back to config
- One-shot worker model
- Site filter hardcoded as `siteNo=[103]`
- No broader data warehouse layer confirmed

