# Wayfarer

Wayfarer is the intended WebPM2 automation worker in the BellBeast ecosystem. On the current `master` branch it is a scaffolded .NET worker solution that establishes the project structure for a future Playwright-based collector, but it does not yet contain a production implementation of the WebPM2 automation pipeline.

## System Role

Within the broader local system:

- `Uroboros` is the backend orchestrator, scheduler, and API listener.
- `BellBeast` is the frontend dashboard and operator interface.
- `Wayfarer` is intended to become the Playwright automation worker for WebPM2 collection.

Important current-state note:

- this README describes the actual `master` branch only
- `master` is a foundation/scaffold branch, not a completed collector

## Purpose

The repository is structured to support a future automation service that would:

- run as a background worker
- log into WebPM2 or related PM web systems
- automate browser navigation with Playwright
- extract work order or maintenance metadata
- normalize collected data into reusable domain structures
- eventually feed downstream systems such as Uroboros and BellBeast

As of the current `master` branch, those behaviors are planned but not yet implemented.

## Repository Layout

Tracked source structure on `master`:

- `Wayfarer.slnx`
- `Wayfarer.Core/`
- `Wayfarer.Playwright/`
- `Wayfarer.Worker1/`
- `README.md`

Tracked source files:

- `Wayfarer.Core/Class1.cs`
- `Wayfarer.Core/Wayfarer.Core.csproj`
- `Wayfarer.Playwright/Class1.cs`
- `Wayfarer.Playwright/Wayfarer.Playwright.csproj`
- `Wayfarer.Worker1/Program.cs`
- `Wayfarer.Worker1/Worker.cs`
- `Wayfarer.Worker1/Wayfarer.Worker.csproj`
- `Wayfarer.Worker1/appsettings.json`
- `Wayfarer.Worker1/Properties/launchSettings.json`

## Architecture Summary

The solution is split into three intended layers:

- `Wayfarer.Core`
  - domain/shared library placeholder
- `Wayfarer.Playwright`
  - browser automation library placeholder
- `Wayfarer.Worker1`
  - executable background worker host

Current implementation status:

- `Wayfarer.Core` contains only an empty placeholder class
- `Wayfarer.Playwright` contains only an empty placeholder class
- `Wayfarer.Worker1` contains a minimal hosted worker that logs a timestamp every second

This means `master` currently demonstrates the hosting model and project boundaries, but not the target automation workflow.

## Entry Points

Primary runtime entry point:

- `Wayfarer.Worker1/Program.cs`

Current startup behavior:

1. Create a host with `Host.CreateApplicationBuilder(args)`
2. Register `Worker` as a hosted service
3. Build and run the host

Background execution entry point:

- `Wayfarer.Worker1/Worker.cs`

Current worker behavior:

- infinite background loop until cancellation
- logs `Worker running at: {time}`
- waits 1 second between iterations

## Runtime and Hosting

Wayfarer `master` is a background worker process, not a web server.

Implications:

- there are no HTTP listener ports configured in this branch
- there are no API endpoints
- the default run mode is console/worker-host execution

Development profile:

- `Wayfarer.Worker1/Properties/launchSettings.json`
  - sets `DOTNET_ENVIRONMENT=Development`

## Project and Package Summary

### Wayfarer.Core

- SDK: `Microsoft.NET.Sdk`
- Target framework: `net10.0`
- Purpose on `master`: placeholder shared library

### Wayfarer.Playwright

- SDK: `Microsoft.NET.Sdk`
- Target framework: `net10.0`
- Purpose on `master`: placeholder automation library

Observed tracked package state:

- no NuGet package references are currently present in the tracked `Wayfarer.Playwright.csproj` on `master`

### Wayfarer.Worker1

- SDK: `Microsoft.NET.Sdk.Worker`
- Target framework: `net10.0`
- Package references:
  - `Microsoft.Extensions.Hosting` `10.0.6`

This project is the only tracked executable in the solution on `master`.

## Configuration

Tracked runtime configuration:

- `Wayfarer.Worker1/appsettings.json`

Current contents:

- standard logging configuration only
- `Default` log level: `Information`
- `Microsoft.Hosting.Lifetime` log level: `Information`

There are no tracked settings yet for:

- target URLs
- login credentials
- polling schedules
- browser options
- output database path
- downstream API addresses

## Database and Storage

There are no tracked database files or storage schemas in the `master` source tree.

Important clarification:

- a `wayfarer.db` file was observed only inside generated build output under an untracked directory outside the solution definition
- that file is not part of the tracked `master` architecture and is not documented here as a supported source artifact

Current tracked storage responsibilities on `master`:

- none beyond normal worker configuration files and build outputs

## Scheduler and Background Tasks

Wayfarer `master` does include a background task host, but only at the scaffold level.

Current scheduling behavior:

- one hosted `BackgroundService`
- continuous loop
- 1-second delay between iterations
- cooperative cancellation via `stoppingToken`

What is not yet implemented:

- cron-style scheduling
- interval configuration
- collector job orchestration
- retry policies
- task persistence
- health supervision

## API Endpoints

There are no API endpoints on the current `master` branch.

The worker:

- does not expose HTTP APIs
- does not host ASP.NET Core
- does not publish health or control routes

If future versions need to serve status or data over HTTP, that capability still needs to be designed and implemented.

## External Integrations

Planned integration direction is implied by the repository naming, project split, and ecosystem context:

- WebPM2 or another browser-only PM interface
- Playwright browser automation
- downstream BellBeast/Uroboros data consumers

Actual implemented integrations on `master`:

- none

There is no tracked code on `master` yet for:

- login flows
- browser launching
- page navigation
- scraping/selectors
- database writes
- REST publishing

## Logging and Error Handling

Current logging/error-handling posture is minimal:

- worker logs an informational heartbeat once per second
- standard worker-host logging pipeline is used
- there is no custom structured logging
- there is no collector-specific error handling because no collector logic exists yet

Operational implication:

- build/run validation confirms the worker host is healthy
- it does not yet validate any automation use case

## Setup Instructions

### Prerequisites

- .NET SDK 10.x preview or compatible SDK capable of building `net10.0`

No browser automation prerequisites are required for the tracked `master` branch because Playwright is not yet wired into the executable path.

### Restore

```powershell
dotnet restore .\Wayfarer.slnx
```

### Build

```powershell
dotnet build .\Wayfarer.slnx
```

### Run

```powershell
dotnet run --project .\Wayfarer.Worker1\Wayfarer.Worker.csproj
```

Expected runtime behavior:

- console/host process starts
- logs a heartbeat message once per second

## Operational Workflow

Actual current workflow on `master`:

1. Start the worker host.
2. Hosted service begins its loop.
3. Worker writes timestamp log messages every second.
4. Process runs until cancellation/shutdown.

Intended future workflow:

1. Start the worker host.
2. Load collector configuration and credentials.
3. Launch Playwright browser session.
4. Authenticate into WebPM2.
5. Navigate and collect target maintenance data.
6. Persist/export normalized data.
7. Repeat on a controlled schedule.

Only the first workflow exists today on `master`.

## Deployment Assumptions

Current code suggests these lightweight assumptions:

- worker/console deployment
- no inbound port exposure required
- local config file support through standard .NET host config

Future production deployment would likely need:

- service hosting or scheduled task execution
- browser runtime dependencies for Playwright
- secure secret management
- output storage and/or downstream publishing target
- process supervision and restart strategy

## Known Limitations

- `master` is mostly scaffolding and not yet a production collector.
- Core and Playwright projects are placeholders only.
- No WebPM2 automation logic exists in tracked `master` files.
- No data model, persistence layer, or API publishing contract is defined yet.
- No tests are present.
- No health endpoint or control surface exists.

## Future Development Notes

Recommended next steps for the repository:

- define domain models in `Wayfarer.Core`
- implement browser session and page collectors in `Wayfarer.Playwright`
- decide on output persistence format and schema
- define how collected data is consumed by Uroboros and/or BellBeast
- add structured logging, retries, and failure capture
- add configuration for credentials, URLs, polling cadence, and browser runtime
- add tests around parsing and normalization logic
- decide whether the worker remains headless-only or also exposes health/status APIs

## Validation Status

This README is based on direct inspection of the tracked `master` branch contents and solution definition.

Validation performed:

- `git status`
- `git branch`
- `git checkout master`
- `git pull origin master`
- inspected `Wayfarer.slnx`
- inspected all tracked source files in `Wayfarer.Core`, `Wayfarer.Playwright`, and `Wayfarer.Worker1`
- built the solution successfully with:

```powershell
dotnet build .\Wayfarer.slnx
```

Observed result:

- build succeeded
- 0 warnings
- 0 errors
