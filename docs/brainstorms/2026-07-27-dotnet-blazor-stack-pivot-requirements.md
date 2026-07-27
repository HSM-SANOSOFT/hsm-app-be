---
date: 2026-07-27
topic: dotnet-blazor-stack-pivot
---

# .NET / Blazor Stack Pivot — Freeze and Rebuild

## Summary

Land the in-flight TypeScript work, freeze it behind a tag, then rebuild the platform as a
.NET 10 / ASP.NET Core / Blazor Server solution on a dedicated anchor branch that every rewrite
branch forks from. The first deliverable is the minor release — the six legacy microservices'
external contract, real auth and user handling, and a thin Blazor admin UI — rebuilt in C#, with a
written definition of done fixed before the build starts.

---

## Problem Frame

HSM is a greenfield replacement for **SanoSoft**, the hospital's production system (server-rendered
PHP on Oracle). SanoSoft's codebase carries heavy duplication, commented-out copy-paste, no formal
architecture, and a schema that has degraded over years of unmaintained change. It cannot scale
horizontally and cannot absorb feature requests at a useful rate.

The replacement effort so far produced a TypeScript monorepo — NestJS API, BullMQ worker, Angular
web, shared packages — currently at 43 REST routes and 194 tracked execution flows across seven
modules: auth, users, roles, settings, templates, documents, communications, plus early clinical
scaffolding. `docs/ARCHITECTURE-DECISIONS.md` records the decision to abandon that stack for
.NET 10 / ASP.NET Core / EF Core / Blazor Server, on grounds of EF Core change tracking for clinical
aggregates, the healthcare interop ecosystem, Active Directory support, and one-developer-ships-a-
full-module velocity.

Two facts shape how that pivot has to be sequenced, and neither is currently reflected in the
architecture document.

**The team is one person plus an AI assistant.** There are no other developers and none are being
hired. Every hiring-pool and onboarding argument in the architecture document describes a future
state that only exists if the port gets approved.

**The port is not yet approved.** What exists today is a proof of concept. The near-term objective
is a production release substantial enough to argue that the platform rewrite is the right path —
after which the remaining ~40 modules get funded. There is no external deadline, no budget cycle,
and no board; the approver is family and the pace is self-set. That removes schedule pressure and
replaces it with a different exposure: with no deadline and no second person, work can continue
indefinitely without ever reaching a state anyone would call shippable.

Separately, the architecture document states that sunk cost is "near zero — only auth/users modules
exist." That is wrong by roughly an order of magnitude, and its §9 sequencing is written as though
building from nothing.

---

## Actors

- A1. Solo developer: the only person writing code, working with an AI assistant. Absorbs the C#,
  ASP.NET Core, EF Core, and Blazor learning curve alone.
- A2. Management: decides whether to fund the ~40-module port. Family, not a corporate gate.
  Judges on shipping speed and on symptoms of unreliability they personally experience.
- A3. Integration consumer: backend systems and the legacy app's replacement, authenticating with
  long-lived tokens. The primary consumer of the minor release's API surface.
- A4. Staff user: hospital personnel reaching the system through a browser on old, wired, stationary
  desktop hardware. In the minor release, reaches only the administrative surfaces in R13.
- A5. Legacy SanoSoft: stays in production writing Oracle throughout. Cannot be modified; Oracle
  permits `SELECT` and `UPDATE` only.

*Key Flows omitted: this work is a sequencing and architecture decision, not a user-facing feature.
The minor release's own flows are already specified in
`docs/brainstorms/2026-07-23-core-services-consolidation-requirements.md`, which this document
retargets rather than replaces.*

---

## Branch Model

```
feat/ssr-auth-migration ──┐
                          ▼
development ──────────────●─────────────────────────────────●──────►
                        merge                             merge
                        + TAG                            (when done)
                          │                                 ▲
                          └──► anchor ──●──────────────────┘
                                        │   normal work resumes
                          removes TS ───┘   from development
                                        │
                                        ├──► rewrite/foundation
                                        ├──► rewrite/auth
                                        └──► rewrite/...
```

The tag on `development` is the frozen TypeScript reference specification. The anchor's first
commit removes the TypeScript stack; every rewrite branch forks from the anchor after that point.

---

## Requirements

**Branch strategy and freeze**

- R1. The current working state is committed to `feat/ssr-auth-migration` — the modified
  `CLAUDE.md`, and the untracked `AGENTS.md`, `.claude/skills/`, and `docs/` additions.
- R2. `feat/ssr-auth-migration` is merged into `development` and the merge is tagged. That tag is
  the frozen TypeScript reference specification, and the merge is the last TypeScript-era change to
  `development`.
- R3. An anchor branch is created from `development`. Every rewrite branch forks from the anchor,
  never from `development`.
- R4. The anchor merges into `development` only once the minor release meets its definition of done.
  Normal branching from `development` resumes after that merge.

**Repository cleanup**

- R5. The anchor's first commit removes the Node and TypeScript stack: the tooling manifests,
  workspace configuration, and lockfiles at the repository root, together with the `apps/` and
  `packages/` source trees. The commit is explicit and reviewable rather than a silent tree change.
- R6. `docs/` is retained unchanged. `docker/` and `.devcontainer/` are retained and retargeted —
  infrastructure services stay, Node application service definitions and the Node toolchain are
  replaced with the .NET SDK.

**Rebuild**

- R7. The new .NET solution becomes the sole active codebase. No parallel-run, no per-module
  cutover, no shared runtime between the two stacks.
- R8. The frozen TypeScript code is treated as a **reference specification** — the authoritative
  record of which endpoints exist and what they return — not as code to transliterate. This is the
  same status already assigned to the six legacy microservice repositories.
- R9. The client-isolation boundary is established before the first module is built: interactive UI
  components live in a library that can reference only the shared contracts, making an application-
  or infrastructure-layer reference a compile error rather than a convention. Because the minor
  release ships a UI (R13), this boundary is on the minor's critical path, not deferred past it.

**Minor release**

- R10. The minor release reproduces the external contract of the six legacy `hsm-be-core-*`
  microservices, as scoped in the 2026-07-23 consolidation document.
- R11. Auth and user handling reach behavioral parity with the frozen TypeScript monolith: password
  login, session and refresh, integration-token authentication, PIN/OTP issuance and validation with
  throttling and lockout, password reset, username recovery, role assignment, and CSRF protection.
- R12. The documents capability — blob-backed storage with versioning and presigned access — is
  included, because the contract depends on it.
- R13. The minor release ships a **thin Blazor Server administrative UI**, covering only surfaces
  that have no other home: sign-in, user and role administration, integration account provisioning
  and token issuance, application settings, and document management. It is not a rebuild of the
  frozen Angular application.
- R14. A written definition of done for the minor release is fixed **before** rebuild work starts,
  enumerating which capabilities must be complete and what "complete" means for each.
- R15. The minor targets PostgreSQL only. The rebuilt application holds no runtime connection to
  Oracle, consistent with the pg-native model established on 2026-07-02.

**Document maintenance**

- R16. `docs/ARCHITECTURE-DECISIONS.md` is amended, not replaced: the sunk-cost line corrected, the
  SanoSoft and Oracle context it currently omits added, and §9 sequencing rewritten to begin from
  freeze-and-rebuild rather than from zero.
- R17. `docs/brainstorms/2026-07-23-core-services-consolidation-requirements.md` is marked superseded
  as to delivery vehicle, recording that its TypeScript-targeted release was deliberately killed and
  its contract scope carried into the C# rebuild.

**Management case**

- R18. The case made to management leads with shipping speed, which is demonstrable, rather than with
  reliability, which is not directly observable.
- R19. Where reliability is invoked, it is expressed as symptoms A2 has personally experienced —
  change requests taking weeks, fixes that break other things, figures that need manual
  reconciliation, single-person dependency on the original system. Each symptom is confirmed to be
  real before it is used in the argument.

**Convergence**

- R20. Progress is measured against the R14 definition of done at fixed intervals, not judged ad hoc.
- R21. If the R14 definition is revised to add scope twice, the sequencing decision in this document
  is re-opened rather than the timeline silently extended again.

---

## Acceptance Examples

- AE1. **Covers R3.** Given a new rewrite module is about to start, when its branch is created, it
  forks from the anchor — not from `development`, and not from another rewrite branch.
- AE2. **Covers R5, R8.** Given the anchor no longer contains any TypeScript source, when a behavior
  needs to be checked during the rebuild, it is read from the tagged commit on `development` rather
  than restored into the working tree.
- AE3. **Covers R8.** Given a behavior implemented in the frozen TypeScript code, when the equivalent
  capability is built in C#, the TypeScript implementation is read to determine *what* the behavior
  is and then designed fresh — not line-by-line translated, and not treated as binding on internal
  structure.
- AE4. **Covers R13.** Given an Angular feature outside the administrative set — onboarding, patient,
  workspace, profile, template authoring — when the minor's UI scope is questioned, that feature is
  excluded and deferred to a post-approval module.
- AE5. **Covers R14, R20.** Given the R14 definition names a fixed set of capabilities, when a review
  interval arrives, each capability is marked complete or not against that written definition, and
  the remaining set is what determines whether the release ships.
- AE6. **Covers R14, R21.** Given the R14 definition has already been revised once to add a
  capability, when a second scope addition is proposed, the response is to re-open the sequencing
  decision — not to accept the addition and continue.
- AE7. **Covers R19.** Given a candidate reliability symptom such as "reports need manual
  reconciliation," when it cannot be tied to something A2 has actually experienced, it is dropped
  from the argument rather than asserted.

---

## Success Criteria

- A2 sees a working production release, uses the administrative UI directly, and agrees to fund the
  remaining ~40 modules.
- The minor release reaches the R14 definition of done without the definition having been rewritten
  more than once.
- The anchor branch's working tree contains no Node or TypeScript artifacts, and the full
  TypeScript history remains reachable from the tag on `development`.
- A1 can build a new module end-to-end in the .NET solution — domain, persistence, handler, UI
  service, Blazor page, REST controller — without consulting the frozen TypeScript code for
  structural guidance.
- The client-isolation boundary holds: an attempt to reference the application layer from the UI
  component library fails to compile.
- `/ce-plan` can produce a rebuild sequence from this document without having to decide what the
  minor release contains, how branches are organized, or what the pitch to A2 is.

---

## Scope Boundaries

- Parallel-run or per-module TypeScript-to-C# cutover. Both were considered and rejected in favor of
  a clean freeze.
- Shipping the minor release on the TypeScript stack first.
- Rewriting git history, squashing, or otherwise removing the TypeScript past from the repository.
  The anchor removes files going forward; history stays intact behind the tag.
- Full Blazor parity with the frozen Angular application — onboarding, patient, workspace, profile,
  template authoring, the rich editor, and PWA installability are all outside the minor release.
- Clinical modules beyond what the minor's external contract requires — CPOE and orders, LIS, RIS,
  EMR encounters, billing, pharmacy, scheduling.
- PACS deployment, DICOM viewing, HL7 v2 instrument interfaces, and FHIR façade work beyond the
  contract surface that already exists.
- Mobile: Blazor WebAssembly, per-request render mode selection, and auth across render modes.
- The one-time bulk Oracle-to-PostgreSQL data migration at cutoff.
- Hiring, onboarding, and team-growth planning.
- Solution layout, project structure, and rebuild ordering — these belong to planning.
- LXC provisioning and the backup/restore drill.
- Porting the TypeScript database entity-generator subsystem. EF Core migrations replace it.

---

## Key Decisions

- **Merge before freezing, rather than parking the branch unmerged.** Landing
  `feat/ssr-auth-migration` first makes `development` the fullest TypeScript state, so a single tag
  captures everything and the anchor inherits the planning documents without a cherry-pick.
- **An anchor branch rather than rewriting on `development` directly.** `development` stays a
  working mainline throughout, the rewrite's history is reviewable as a unit before it lands, and
  abandoning the rewrite costs one branch deletion.
- **Deletion covers source, not only tooling.** Leaving 48k lines of dead TypeScript beside a .NET
  solution defeats the clean structure the anchor exists to create. Safe because the tag preserves
  everything.
- **A thin administrative UI rather than API-only or full Angular parity.** API-only would give A2
  nothing to look at, which defeats R18. Full parity would put the entire frontend ahead of the
  release. The administrative set is the smallest UI that is genuinely required — integration
  accounts and roles have to be provisioned somewhere — and it exercises the client-isolation
  boundary on a real but small surface.
- **Freeze-and-rebuild over parallel-run.** A parallel or incremental cutover would leave two stacks
  in production maintained by one person. The absence of a deadline makes the slower single-stack
  path affordable.
- **The minor release is rebuilt in C# rather than shipped in TypeScript.** Shipping it in TypeScript
  would reach A2 sooner, but would commit the funded port to TypeORM — whose lack of change tracking
  the architecture document identifies as a patient-safety-grade defect for clinical aggregates.
  Switching stacks after approval means switching under a delivery commitment.
- **The change-tracking argument does not apply to the minor release itself.** Auth, users,
  communications, documents, templates, and patient lookup contain no clinical aggregates with child
  collections. The pivot's strongest technical justification pays off in the modules that follow
  approval, not in this release.
- **Blazor Server replaces Angular; decision #3 in the architecture log stands unamended.** The
  Angular application is frozen along with the rest of the TypeScript stack.
- **Reliability is not the headline of the pitch.** Every deficiency A1 identified in SanoSoft —
  duplication, dead code, no architecture, no horizontal scaling — is invisible to A2 and reads as
  developer preference when presented directly.
- **The definition of done is written before the build, not judged during it.** With one developer
  and no deadline, the failure mode is non-convergence rather than lateness.

---

## Dependencies / Assumptions

- `feat/ssr-auth-migration` is currently 15 commits ahead of `development` and 0 behind, so R2's
  merge is expected to be a fast-forward or a trivial merge.
- RustFS S3 compatibility must be validated within the minor release, because R12's documents
  capability depends on blob storage. Multipart upload, presigned URLs, and object versioning are the
  specific surfaces to confirm.
- A2's willingness to fund the port is assumed to rest on demonstrated shipping speed. This has not
  been tested and no specific reliability incident has been named — the reliability half of the case
  currently has no confirmed target.
- SanoSoft remains in production for the entire duration and is not modified.
- No additional developers join during the minor release.
- The .NET, EF Core, Blazor, and ASP.NET Core learning curve is absorbed while rebuilding auth —
  the least forgiving surface to learn a stack on, and an accepted cost of this sequencing.

---

## Outstanding Questions

*No questions remain that block planning.*

### Deferred to Planning

- [Affects R3][User decision] Anchor branch name.
- [Affects R6][Technical] Fate of `kubernetes/` and `scripts/` — retain, retarget, or remove.
- [Affects R6][Technical] Which `docker/` service definitions survive, and whether the .NET
  application runs as a self-contained binary under systemd rather than in a container.
- [Affects R8][Technical] Whether the frozen contract is additionally captured as a machine-readable
  artifact (OpenAPI or equivalent) at the tag, so the specification is consultable without checking
  out TypeScript.
- [Affects R9][Needs research] Concrete project structure that enforces the client-isolation
  boundary at compile time.
- [Affects R11][Needs research] How integration-token authentication and the PIN/OTP flows map onto
  ASP.NET Core's authentication primitives.
- [Affects R13][Needs research] MudBlazor versus Syncfusion for the administrative UI.
- [Affects R13][User decision] Whether the administrative UI needs Spanish/English localization in
  the minor release, or ships Spanish-only. The frozen Angular application carried full i18n.
- [Affects R14][Technical] Rebuild order within the minor release.
