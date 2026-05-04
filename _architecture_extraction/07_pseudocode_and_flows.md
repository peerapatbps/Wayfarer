# 07 Pseudocode and Flows

## Uroboros Scheduler Task Execution

```text
initialize EngineContext
load runtime task config from engine_admin.db
register known tasks
start Scheduler, WebListener, TriggerLoop

every 250 ms:
  read task config snapshot
  for each catalog task:
    if force-run stamp is near-now:
      pull next run earlier
    if task is not due:
      continue
    if global gate paused:
      advance next-run phase only
      continue
    if task enabled:
      enqueue task instance
    advance next-run phase

Scheduler loop:
  read task from channel
  wait for semaphore slot
  run task with linked cancellation + timeout
  log start / success / cancel / failure
  update health tracker
  release semaphore
  if coalesced rerun pending:
    enqueue one more execution
```

## BellBeast Dashboard Data Refresh

```text
browser loads MHxView
client JS restores slot layout from localStorage
for each slot:
  call MHxView?handler=Slot&key=<BLOCK>
  inject partial HTML
  initialize corresponding JS module(s)

module data refresh pattern:
  fetch BellBeast /api/... endpoint
  if endpoint is local:
    query SQLite-backed data
  else:
    proxy request to Uroboros localhost:8888
  receive JSON or file response
  update block visuals / chart / summary state
```

## Aquadat Query / Write

```text
receive begin/end/token/config mapping request
if token missing:
  enroll/login to Aquadat

build union of config mappings by key
group configs by plant and station type
for each plant group:
  call Aquadat data API
  parse JSON rows
  append to master row set

build pivot/master table
normalize time columns and baseline rows
trim to requested logical range

if CSV output requested:
  export subtable per key

if DB output requested:
  clear target AQ tables
  convert rows to data dictionary
  upsert into AQ_readings_narrow_v2 or AQ_readings_FWS_v2
  refresh latest views
```

## Wayfarer Worker Execution

```text
host starts Worker
collect snapshot records via IPmCollector
save snapshot index rows to wayfarer.db
reload index records
collect detail payload JSON for each work order
parse and persist normalized detail tables
log finish
stop host so process exits
```

## WebPM2 Login / Token / Work Order Fetch

```text
launch Chromium with configured headless mode
open browser context and page
navigate to WebPM2 base URL

if redirected to ADFS:
  try rotated password candidates
else if employee login choice shown:
  click employee login
  wait for ADFS
  try rotated password candidates

on successful login:
  if working password differs from stored config:
    write new password to appsettings.json

for list collection:
  navigate to work-order page
  wait for token endpoint response
  extract access_token
  collect cookies from browser context
  call WebPM2 work-order list API with bearer token + cookies
  page through results until cutoff or total reached

for each work order:
  navigate to detail page
  capture token again if needed
  call detail API /api/api/wo/{woNo}
  store raw JSON envelope
```

## Error Handling / Retry / Degradation Flow

```text
Uroboros:
  wrap task execution in try/catch
  record health failure and continue scheduler
  return JSON 500 for HTTP handler exceptions
  treat SQLite busy/locked as recognized operational conditions

BellBeast:
  for proxy APIs, pass through downstream status and body
  convert auth redirects for /api/* into 401/403

Wayfarer:
  retry login across rotated password candidates
  downgrade network-idle wait timeout to warning if DOM already loaded
  throw on missing token, HTTP failure, or exhausted login candidates
```
