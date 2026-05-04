# 06 Tables for Academic Report

## Component Role Table

| Component | Role |
| --- | --- |
| Uroboros scheduler | Runs timed operational collection and processing tasks |
| Uroboros HTTP listener | Provides local machine API/control plane on port `8888` |
| BellBeast web app | Provides browser dashboard and reporting UI on port `5082` |
| Wayfarer Playwright collector | Acquires WebPM2 work-order data through browser-assisted authentication |
| SQLite stores | Persist runtime config, operational readings, and maintenance snapshots |

## Repository Responsibility Table

| Repository | Primary responsibility |
| --- | --- |
| Uroboros | Data engine, task orchestration, Aquadat service, summary APIs |
| BellBeast | Dashboard, proxy/API facade, user/admin web access |
| Wayfarer | WebPM2 data acquisition and snapshot persistence |

## Endpoint / API Table

| Runtime owner | Endpoint pattern | Function |
| --- | --- | --- |
| Uroboros | `/admin/*` | Scheduler control and status |
| Uroboros | `/api/process` | Aquadat processing/export |
| Uroboros | `/api/*/summary` | Dashboard summary data |
| BellBeast | `/api/backend-config` | Front-end backend path exposure |
| BellBeast | `/api/wayfarer/*` | Wayfarer list/detail/export access |
| BellBeast | `/api/*` proxy routes | Browser-to-Uroboros relay |

## Database / Storage Table

| Database / file | Repository | Purpose |
| --- | --- | --- |
| `engine_admin.db` | Uroboros | Task runtime settings |
| `data.db` | Uroboros | Main operational SQLite store |
| `chem.db` | Uroboros | CHEM/event-related store |
| `aqtable.db` | Uroboros/BellBeast | Aquadat metadata / lookup |
| `wayfarer.db` | Wayfarer/BellBeast/Uroboros | WebPM2 snapshot database |
| `wayfarer_meta.db` | BellBeast/Uroboros | Wayfarer metadata / departments |

## Background Task Table

| Task / worker | Repository | Purpose |
| --- | --- | --- |
| `TriggerLoop` | Uroboros | Computes due work from runtime intervals |
| `Scheduler` | Uroboros | Queues/runs tasks with concurrency and timeout control |
| `wayfarer.pm.collect` | Uroboros | Invokes Wayfarer collector |
| `Worker` | Wayfarer | One-shot collection of list and detail payloads |

## External Integration Table

| External system | Integration style | Repositories |
| --- | --- | --- |
| Aquadat | HTTP enroll/data APIs | Uroboros, BellBeast login |
| WebPM2 | Browser automation + API calls | Wayfarer |
| ADFS | Browser login automation | Wayfarer |
| Google Drive | API library integration | Uroboros |
| Internal CGI endpoints | HTTP polling | Uroboros |
| SmartMap | HTTP proxy | BellBeast |

## Data Source Table

| Data source | Main consumer | Output |
| --- | --- | --- |
| Aquadat | Uroboros | SQLite + CSV/report outputs |
| Internal plant CGI | Uroboros | Operational summary tables |
| WebPM2 | Wayfarer | Normalized maintenance snapshot DB |
| Excel lab files | Uroboros | Imported lab summary data |

## Report Mapping Table

| Report target | Best supporting repository | Why |
| --- | --- | --- |
| OPA MHxView Dashboard | BellBeast | UI/dashboard modules and web delivery |
| LL1 Aquadat as a Service | Uroboros | Query/write/export/storage workflow |
| LL2 WebPM2 as a Service | Wayfarer | Playwright/token/API/snapshot pipeline |

## Limitation / Future Work Table

| Topic | Current state |
| --- | --- |
| Secret handling | Sensitive values appear in local config patterns; should be hardened |
| Deployment automation | Not confirmed from code |
| HA / scale-out | Not confirmed from code |
| Centralized monitoring | Not confirmed from code |
| Formal service contracts | Partial, route-based only |

