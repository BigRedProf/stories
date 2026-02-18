# AGENTS.md – BigRedProf.Stories

This repository contains the BigRedProf.Stories library.

Stories capture a series of things over time using an append-only model.
This is an event-sourced style library where determinism, clarity, and
backward compatibility matter.

This file defines expectations for AI agents and contributors.

---

## Authoritative Coding Standards

All formatting, organization, naming, nullability, and defensive programming
rules are defined in:

    CODING_GUIDELINES.md

That document is the authoritative source of truth.

If there is any conflict between this file and CODING_GUIDELINES.md,
follow CODING_GUIDELINES.md.

---

## Build Instructions

Restore and build:

    dotnet restore
    dotnet build -c Release

Run tests (if present):

    dotnet test

The SDK version is pinned in `global.json`.

---

## Architectural Principles (Stories-Specific)

- Stories are append-only.
- Events must be immutable.
- Public models are long-lived contracts.
- Serialization format changes require explicit approval.
- Determinism is critical.
- Avoid hidden side effects.
- Favor explicitness over cleverness.

---

## Testing Expectations

- All behavior changes must include or update tests.
- Tests must be deterministic.
- Never use randomness in tests.
- Prefer fixed GUIDs, timestamps, and constants.

---

## What NOT To Do

- Do not change event shapes without explicit instruction.
- Do not rename public APIs without confirmation.
- Do not introduce implicit behavior.
- Do not remove backward compatibility without discussion.

---

## When Unsure

Ask before:

- Changing schemas
- Modifying serialization
- Altering public contracts
- Introducing new architectural patterns
