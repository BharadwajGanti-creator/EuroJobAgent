# ADR 002: Tracker state source in AKS

## Context
`TrackerStore.Load()` reads `tracker.json` from local disk. This works in
GitHub Actions (the cron job commits the file back to the repo it's running
in) but is meaningless in a long-running AKS pod — the pod's filesystem
never sees what the separate daily workflow commits to git, and Phase 1's
own probe-break exercise demonstrated this divergence directly (the pod's
local copy went stale the moment it was manually edited).

## Decision
The deployed dashboard fetches `tracker.json` from the GitHub raw-content
URL at request time, gated behind the `TRACKER_JSON_URL` env var (unset
locally, so dev/local behavior is unchanged).

## Alternatives considered
A PersistentVolumeClaim would avoid the per-request network dependency,
but adds a stateful volume to manage and back up for read-mostly data that
already lives, version-controlled, in git. Given the data only changes
once a day via the existing cron job, an HTTP fetch's added latency and
network dependency is the cheaper, more honest tradeoff.

## Consequences
Dashboard now depends on GitHub's raw-content endpoint being reachable at
request time. Acceptable for a personal, low-traffic project; would need
revisiting (e.g. a proper cache or PVC) if this were serving real traffic
or GitHub availability became a hard dependency.
