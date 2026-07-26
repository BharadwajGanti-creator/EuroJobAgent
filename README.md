# EuroJobAgent

An agentic C#/.NET pipeline that searches for backend .NET/C# roles in target countries, scores
them against a candidate profile using an LLM agent, deterministically boosts known visa-sponsor
companies, drafts a tailored resume emphasis + cover letter for the strongest matches, tracks
everything across runs, and emails a daily digest. Runs unattended on a GitHub Actions schedule.

This is a separate project from an earlier job-search agent — same author, different design:
this one is scoped specifically around a live visa-sponsorship search (Netherlands first) rather
than a general SDE search, and adds persistent status tracking, a drafting agent, and email
delivery.

## Architecture

```
Program.cs (cron mode)
  │
  ├─ CandidateProfile  ← candidate_profile.json (written from a CI secret, never committed)
  ├─ TrackerStore      ← tracker.json (committed back to the repo each run — the whole "database")
  │
  ├─ JobMatcher.PlanQueriesAsync   (AIAgent #1: QueryPlanner, Gemini 2.5 Flash Lite)
  ├─ JoobleJobSource.SearchAsync   (Jooble API, per query per target location)
  ├─ JobMatcher.ScoreJobsAsync     (AIAgent #2: Matcher — fit score, reason, missing skill)
  ├─ deterministic target-company boost (string match against a known-sponsor list, NOT left to the LLM)
  │
  ├─ for each NEW job scoring ≥ DRAFT_THRESHOLD:
  │     DraftAgent.DraftAsync      (AIAgent #3: Drafter — tailored summary/bullets/cover letter,
  │                                  grounded only in the candidate's real experience bullets)
  │
  ├─ TrackerStore.Save             (persist all new jobs, Status = "New")
  └─ EmailService.SendDigestAsync  (SendGrid v3 Mail Send API, one digest email per run)
```

Three agents, each with one job and a strict "return only JSON" contract, mirrors the pattern
used successfully in the author's first agentic project (query planning → retrieval → scoring),
extended with a third agent for drafting.

## What it deliberately does NOT do

- **Does not auto-apply anywhere.** It emails you a draft. You review it, edit it, and submit the
  application yourself through the employer's own site.
- **Does not trust the LLM to know who sponsors visas.** The target-company list is a hardcoded,
  manually-verified list (see `AppConfig.TargetCompanies`); the LLM only judges role/stack/domain fit.
- **Does not invent resume content.** The drafting agent is instructed to only reorder/rephrase the
  bullets it's given — never to fabricate employers, numbers, or skills.

## Setup

1. **Candidate profile.** Copy `candidate_profile.example.json`, edit it to match your real
   experience (it's already pre-filled with a real profile as a worked example — replace with
   your own), and save the JSON as a repo secret named `CANDIDATE_PROFILE_JSON`
   (Settings → Secrets and variables → Actions → New repository secret). Do not commit the real
   file — it's gitignored.
2. **Gemini API key.** If you already have one from another project, reuse it. Otherwise get one
   at [aistudio.google.com](https://aistudio.google.com/) and save it as secret `GEMINI_API_KEY`.
3. **Jooble API key.** Register at [jooble.org/api/about](https://jooble.org/api/about) and save
   it as secret `JOOBLE_API_KEY`.
4. **SendGrid.** Sign up at [sendgrid.com](https://sendgrid.com), verify a sender identity (a
   real email address you control), create an API key, and save it as secret `SENDGRID_API_KEY`.
   Save the verified sender address as secret `EMAIL_FROM` and your own inbox as secret `EMAIL_TO`.
5. **Test locally first**, if you have the .NET 10 SDK installed:
   ```
   export GEMINI_API_KEY=...
   export JOOBLE_API_KEY=...
   export SENDGRID_API_KEY=...
   export EMAIL_FROM=you@yourdomain.com
   export EMAIL_TO=you@gmail.com
   cp candidate_profile.example.json candidate_profile.json   # then edit it with your real data
   dotnet run -- cron
   ```
   Check the console output and `tracker.json` before trusting a scheduled run.
6. **Push to GitHub**, then run the workflow once manually (Actions tab → Daily EU job search →
   Run workflow) before letting the 07:00 UTC schedule take over.

## Updating a job's status after you apply

```
dotnet run -- mark <jobId-or-prefix> Applied
git add tracker.json && git commit -m "Mark job as applied" && git push
```

`jobId` is the short hash shown in the email digest and in `tracker.json`; any unambiguous prefix
of it works.

## Known limitations / things to revisit

- **Job identity is a hash of the posting URL.** If Jooble ever returns the same posting under a
  slightly different tracking URL, it will be treated as a new job. Cheap and effective in
  practice, but not bulletproof — worth watching in `tracker.json` over the first few weeks.
- **No evaluation harness in this project** (unlike the earlier agentic-job-search project's
  `Eval.cs` + golden set). Deliberately left out to keep this build scoped to what was asked for;
  straightforward to add the same golden-set pattern later if useful.
- **Single job source (Jooble).** Chosen because it already covers the Netherlands with a proven
  track record from the other project. Add a second `IJobSource` implementation if coverage gaps
  show up.
- **This code has not been compiled or run** by the assistant that wrote it (no .NET SDK reachable
  in that sandbox) — treat the first local `dotnet run -- cron` as the real test, not this file
  existing.
