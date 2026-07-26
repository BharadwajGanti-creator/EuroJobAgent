# EuroJobAgent

An agentic C#/.NET pipeline that searches for backend .NET/C# roles in target countries, scores
them against a candidate profile using an LLM agent, deterministically boosts known visa-sponsor
companies, drafts a tailored resume emphasis + cover letter for the strongest matches, tracks
everything across runs, and emails a daily digest. Runs unattended on a GitHub Actions schedule,
with a browsable dashboard, an evaluation harness, and a test suite alongside it.

This is a separate project from an earlier job-search agent (`agentic-job-search`) — same author,
different design: this one is scoped specifically around a live visa-sponsorship search
(Netherlands first) rather than a general SDE search, and adds persistent status tracking, a
drafting agent, and email delivery. See "Design notes" below for an honest comparison between the
two.

## Architecture

```
Program.cs
  ├─ mark <id> <status>   → TrackerStore.Mark          (CLI: update a job's status locally)
  ├─ eval                 → Eval.RunAsync               (precision/recall vs. golden_set.json)
  ├─ dump                 → fetch a fresh unlabelled batch → jobs_dump.json (grows the golden set)
  ├─ cron                 → RunCronAsync                (the daily pipeline, see below)
  └─ (no args)            → RunWebMode                  (ASP.NET Core: dashboard + live API)

RunCronAsync (wrapped in a top-level try/catch → SendFailureAlertAsync on any unhandled exception)
  ├─ TrackerStore.Load()                    ← tracker.json (committed back to the repo each run)
  ├─ SearchService.RunAsync
  │    ├─ JobMatcher.PlanQueriesAsync        (AIAgent #1: QueryPlanner, Gemini 2.5 Flash Lite)
  │    ├─ JoobleJobSource.SearchAsync        (Jooble API, per query per target location, retried)
  │    ├─ JobMatcher.ScoreJobsAsync          (AIAgent #2: Matcher — fit score, reason, missing skill)
  │    │    └─ throws MatcherIncompleteException if the LLM drops a job index; SearchService
  │    │       catches THIS specific exception and degrades gracefully (see "Design notes")
  │    └─ TargetCompanyMatcher.ApplyBoost    (deterministic string match, NOT left to the LLM)
  ├─ DraftAgent.DraftAsync (per new job ≥ DRAFT_THRESHOLD)  (AIAgent #3: Drafter)
  ├─ TrackerStore.Save()
  └─ EmailService.SendDigestAsync            (SendGrid v3 Mail Send API, retried)

RunWebMode
  ├─ GET  /api/tracker   → every tracked job, for the dashboard
  ├─ POST /api/mark      → update a job's status from the browser
  └─ POST /api/run       → trigger RunCronAsync on demand ("Run now" button)
```

Three agents, each with one job and a strict "return only JSON" contract. `Retry.WithBackoffAsync`
wraps every external call (Jooble, all three Gemini agent calls, SendGrid) with exponential backoff.

## What it deliberately does NOT do

- **Does not auto-apply anywhere.** It emails you a draft, or shows it in the dashboard. You review
  it, edit it, and submit the application yourself through the employer's own site.
- **Does not trust the LLM to know who sponsors visas.** The target-company list is a hardcoded,
  manually-verified list (`AppConfig.TargetCompanies`); the LLM only judges role/stack/domain fit.
- **Does not invent resume content.** The drafting agent is instructed to only reorder/rephrase the
  bullets it's given — never to fabricate employers, numbers, or skills.

## Design notes: how this compares to the first project, and why some things differ

**Ported forward, adapted:** the evaluation harness (`Eval.cs`, `golden_set.json`, `dump` mode) and
the ASP.NET Core web mode + dashboard are both taken from `agentic-job-search`'s proven pattern —
they were cut from the first build of this project to hit a tight deadline, and added back here
because a "ready to ship" project shouldn't ship without a way to measure match quality or a way to
demo it live instead of trusting a console log.

**Improved over the first project:** `AppConfig.cs` centralises target locations/companies/
threshold via env vars, instead of a `const string` duplicated across `Program.cs` and `Eval.cs`.
`tracker.json` gives real cross-run persistence and a `Status` field — the first project's
`wwwroot/latest_matches.json` is overwritten fresh on every run with no history. The
target-company boost and drafting agent are net-new capabilities the first project never needed,
since it wasn't sponsorship-aware by design.

**Deliberately different, not a straightforward copy:** the first project's matcher *throws* the
moment the LLM drops a job index, with the comment "scores incomplete — don't trust the eval."
Here, `JobMatcher.ScoreJobsAsync` still throws (a distinct `MatcherIncompleteException`, carrying
the partial scores it *did* get) — `Eval.cs` lets that propagate and crash, exactly like the first
project, because an incomplete eval run is worse than no eval run. But `SearchService.RunAsync`
(used by the unattended daily cron pipeline) catches that specific exception, logs it loudly, and
patches in a zero score for the dropped indices instead of losing the whole day's run. The strict
behaviour lives in one place; each caller decides how much of that strictness it can afford.

## Setup

1. **Candidate profile.** Copy `candidate_profile.example.json`, edit it to match your real
   experience (it's pre-filled with a real profile as a worked example), and save the JSON as a
   repo secret named `CANDIDATE_PROFILE_JSON`. Do not commit the real file — it's gitignored.
2. **Gemini API key** — [aistudio.google.com](https://aistudio.google.com/) → secret `GEMINI_API_KEY`.
3. **Jooble API key** — [jooble.org/api/about](https://jooble.org/api/about) → secret `JOOBLE_API_KEY`.
4. **SendGrid** — sign up, verify a sender identity, create a Restricted-Access API key scoped to
   Mail Send only → secrets `SENDGRID_API_KEY`, `EMAIL_FROM` (the verified sender), `EMAIL_TO`.
5. **Test locally first**:
   ```
   export GEMINI_API_KEY=...
   export JOOBLE_API_KEY=...
   export SENDGRID_API_KEY=...
   export EMAIL_FROM=you@yourdomain.com
   export EMAIL_TO=you@gmail.com
   cp candidate_profile.example.json candidate_profile.json   # then edit with your real data
   dotnet test              # run the test suite first
   dotnet run -- cron       # then the real pipeline
   ```
6. **Push to GitHub**, add the same secrets there, then run the workflow once manually
   (Actions tab → Daily EU job search → Run workflow) before trusting the 07:00 UTC schedule.

## Building the evaluation set

```
dotnet run -- dump     # fetches a fresh batch of real, unlabelled postings into jobs_dump.json
```
Open `jobs_dump.json`, set `"relevant": true/false` on each entry based on your own judgement, then
move the ones you're confident about into `golden_set.json`. Once it has entries:
```
dotnet run -- eval      # precision/recall of the Matcher agent against your labels
```
`golden_set.json` starts empty — no fabricated examples are shipped with this project.

## Running the dashboard locally

```
dotnet run              # no args = web mode
```
Open `http://localhost:5000` (or whatever port the console prints). Shows every tracked job, lets
you change status inline, and has a "Run now" button that triggers the same pipeline as `cron`.

## Updating a job's status after you apply

Either use the dashboard's dropdown, or:
```
dotnet run -- mark <jobId-or-prefix> Applied
git add tracker.json && git commit -m "Mark job as applied" && git push
```

## Known limitations / things to revisit

- **Job identity is a hash of the posting URL.** If Jooble ever returns the same posting under a
  slightly different tracking URL, it will be treated as a new job (covered by a test documenting
  this explicitly). Cheap and effective in practice, not bulletproof.
- **Single job source (Jooble).** Proven NL coverage from the first project. Add a second
  `IJobSource` implementation if coverage gaps show up.
- **Retry covers transient failures, not malformed responses.** A dropped matcher index isn't
  retried (the same prompt is unlikely to fix it on a second try) — it's degraded gracefully
  instead, per the design notes above.
- **This code was written by an assistant that could not compile or run it** (no .NET SDK reachable
  in that sandbox). Every change was verified by hand (structural checks, cross-referencing, JSON/
  YAML validation) but the real test is `dotnet build` / `dotnet test` on your machine — treat
  compiler errors as expected first-pass friction, not a sign something is fundamentally wrong.
