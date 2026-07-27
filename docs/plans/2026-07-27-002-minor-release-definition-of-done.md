---
title: "Minor release — definition of done"
type: reference
status: active
date: 2026-07-27
origin: docs/plans/2026-07-27-001-feat-dotnet-blazor-rewrite-plan.md (U11)
---

# Minor release — definition of done

Fixed before module work starts (plan U11, requirement R14). Each capability
below is **complete or not** against its named test — no "mostly works". The
release gate (plan U20) walks this list; the remaining set decides whether the
release ships.

## Scope revision counter

**Revisions: 0.**

Per origin requirement R21: every scope *addition* to this document increments
the counter, with a dated entry below. At **two** revisions, the sequencing
decision (rewrite-before-port) is re-opened rather than the timeline extended
again. Removals and clarifications do not count; additions do, however small.

| # | Date | Addition | Why |
|---|------|----------|-----|
| — | — | — | — |

## Reference

The behavioral bar throughout is the frozen contract snapshot
(`docs/reference/2026-07-27-frozen-api-contract.openapi.json`, 58 operations)
and the frozen source at tag `freeze/typescript-2026-07-27`. "Contract tests"
means tests written from that snapshot/source, not from the new implementation.

## Capabilities

### C1 — Identity and authentication (plan U12)

Complete when:
- Every `/v1/auth/*` operation in the frozen snapshot (14 operations) has a
  passing contract test.
- Cookie-session + CSRF mode and integration bearer-token mode coexist with
  separate token stores, each proven by a contract test.
- PIN throttling and lockout thresholds match the frozen implementation's
  values, encoded in tests read from the frozen source.
- Refresh rotation revokes the prior token (test: old token rejected after
  rotation).
- Username recovery is enumeration-safe (test: identical observable response
  with and without an account).

### C2 — Users, roles, settings (plan U13)

Complete when:
- Frozen `/v1/user*` (6 operations) and `/v1/settings` (2 operations) have
  passing contract tests.
- Staff creation and role change are transactional (test: induced failure
  leaves no partial record).
- Settings changes write audit entries recording actor and change.
- Role change invalidates cached authorization state before session expiry.

### C3 — Templates and communications (plan U14)

Complete when:
- Frozen `/v1/templates*` (7) and `/v1/coms/*` (9) operations have passing
  contract tests.
- Webhook signature verification rejects a forged payload (test written
  before the verifier).
- Duplicate webhook events are idempotent.
- Template-in-use deletion is rejected with the domain condition, not a
  database error.
- Email batch/recipient aggregate proves EF change tracking (child removal
  deletes the row) against real PostgreSQL.

### C4 — Documents (plan U15)

Complete when:
- Frozen `/v1/docs*` operations (9) have passing contract tests.
- RustFS multipart upload, presigned URLs (grant + expiry), and object
  versioning are each proven by an integration test **before** the module
  builds on them.
- Document generation produces a PDF via QuestPDF (no browser dependency).
- No document binary is stored in a database column (test inspects schema).

### C5 — Remaining contract surface (plan U16)

Complete when:
- Patient lookup by national identifier works pg-native with frozen
  normalization and not-found behavior, gated by integration credentials.
- Every operation in the frozen snapshot is either implemented with a passing
  contract test or recorded in this document as deliberately dropped, with
  reason. Current deliberate drops: **none**.
- FHIR R4 `Encounter`/`ServiceRequest` write/search surface beyond the
  contract's patient scope is a recorded follow-up, not silently missing
  (origin scope boundary: clinical modules are out of the minor).

### C6 — Admin UI shell (plan U17)

Complete when:
- An authenticated user reaches the shell; unauthenticated visitors are
  redirected to sign-in.
- A component exception is contained by its error boundary; the circuit
  survives.
- Every component reaches the application layer only through
  contracts-declared UI services; the U8 architecture test passes with the
  full component library present.

### C7 — Administrative screens (plan U18)

Complete when each of the **five** screens functions against real data,
end-to-end through the UI:
1. Sign-in.
2. Users and roles (create staff user, assign role; new user can sign in).
3. Integration accounts and tokens (provision account, issue token; token
   authenticates a machine call; secret visible exactly once).
4. Settings (change writes an audit entry visible in the UI or API).
5. Document management (upload and retrieve through the UI).

And: non-admins are blocked by navigation *and* direct URL; **no sixth screen
exists** (a sixth screen is a scope revision — count it).

### C8 — Release infrastructure (plan U20 preconditions)

Complete when:
- `pr-gate` reports real build/unit/integration results (no trivially-green
  guards remaining) on the anchor-to-development PR.
- A clean clone of `development` post-merge builds and runs the application
  with no Node toolchain present.
- The frozen tag still resolves.

## Progress check cadence (R20)

At the completion of each plan unit U12–U18, this document is re-walked and
each capability marked complete/incomplete in the unit's commit message or PR
description. No other status tracking is authoritative.
