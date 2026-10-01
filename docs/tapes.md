# Backing up to tape

`stories backup-all`, `restore-all` and `verify-all` keep a copy of **every story a Stories
service holds** on tape, and put it back. Written for digihouse's Memorial
(BigRedProf/digihouse#397), where Stories is in memory and tapes are what make it durable.

## What is on disk

```
{tapeRoot}/
  2026-W39/                 a generation: one ISO week (UTC)
    manifest.json           the commit point -- see below
    tapes/                  a DiskLibrary of tape files
  2026-W40/
    ...
```

A **generation** is a week. The first `backup-all` of a week copies everything; the rest of
that week append to it. So restoring needs one generation only, the latest, and old ones can
be deleted whole.

Within a generation, every run writes a new **segment** per story that grew: a tape series
holding just the things since the last segment, checkpointed with how many it holds. Segments
are never written to again.

The **manifest** lists each story (by the hash of its id -- all the service knows it by), how
long it is on tape, and its segments in order. It is written to a temporary file and moved
over the old one only after the whole run succeeds. So:

- a run that dies partway leaves segments nothing names, which are harmless, and the next run
  writes that range again -- there is nothing to truncate or repair;
- the manifest on disk is always a whole one.

## The verbs

```bash
stories backup-all  --baseUri http://stories/ --tapeRoot /tapes [--generation 2026-W39]
stories restore-all --baseUri http://stories/ --tapeRoot /tapes [--generation 2026-W39]
stories verify-all  --tapeRoot /tapes [--baseUri http://stories/] [--generation 2026-W39]
```

| Verb | Does | Refuses when |
| --- | --- | --- |
| `backup-all` | lists every story (`GET v1/stories`), writes a segment for each that grew, commits the manifest | a story holds **less** than is on tape: the service has lost history (restarted without a restore), and nothing is committed |
| `restore-all` | replays the generation into the service in batches, then reads the lengths back and compares | the service holds **any** story: restoring underneath newer history would interleave two pasts, and offsets are forever |
| `verify-all` | decodes every segment, checks each holds exactly its checkpoint and they join with no gap, and -- given a service -- that no story on tape is longer than the live one | -- |

Exit codes, for whatever schedules them: **0** done, **2** the tapes have problems, **3**
refused, **4** failed otherwise.

`restore-all` and `verify-all` default to the newest generation with a manifest;
`backup-all` defaults to this week.

## Listing stories

`GET v1/stories` returns every story holding at least one thing, as
`[{ "storyIdHash": "...", "length": 123 }]`, sorted by hash. A story that has only been read
is left out. It exists so that a backup covers everything by construction: a list of stories
kept by an application is a list that forgets one silently.

## The image

`src/StoriesCli/Dockerfile` builds `bigredprofstoriescli`, run as a one-shot container beside
the service with the tape root mounted.
