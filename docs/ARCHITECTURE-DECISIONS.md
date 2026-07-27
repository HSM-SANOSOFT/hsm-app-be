# HSM — Architecture Decisions

Hospital Management System. Greenfield rewrite, porting ~40 modules from a legacy codebase.
Scope: ERP, CRM, HR, HIS, RIS, LIS, PACS integration.

This document records **what was chosen and why**. Implementation detail is deliberately out of scope.

---

## 1. Constraints

| Constraint | Detail |
|---|---|
| **Client hardware** | Hospital PCs are old/low-spec. Minimize client-side processing. **Never measured — see Open Items.** |
| **Network** | Desktops: wired, stationary, sub-millisecond LAN. Mobile (future): hospital WiFi, roaming between APs. |
| **Team velocity** | Ship many modules fast. Stack must be low-friction and easy to onboard into. |
| **Architecture** | Hexagonal (ports & adapters), applied selectively. |
| **Deployment** | On-prem, separate LXC containers on a single Proxmox host. No Kubernetes. **The app must not assume this topology.** |
| **API consumers** | Web UI, future mobile app, third-party integrations, other hospitals (requires FHIR). |
| **Offline** | Not required. The app is useless without live backend data. |
| **Licensing** | Commercial product, possibly deployed for other hospitals. Avoid AGPL / SSPL / ELv2 dependencies. |
| **Hiring** | Loja, Ecuador. Small pool. A real constraint on language choice. |
| **Sunk cost** | Near zero — only auth/users modules exist. |

---

## 2. The Stack

| Layer | Choice |
|---|---|
| **Language / platform** | C# on .NET 10 (LTS) |
| **Backend framework** | ASP.NET Core |
| **ORM** | EF Core + Npgsql, bound concretely to PostgreSQL |
| **Frontend** | Blazor Server (Blazor WASM deferred for mobile) |
| **Component library** | MudBlazor or Syncfusion — to evaluate |
| **Data store** | PostgreSQL |
| **Blob store** | RustFS (S3-compatible, Apache 2.0) |
| **Search store** | PostgreSQL FTS + Meilisearch, split by collection |
| **In-memory store** | Redis |
| **Observability** | OpenTelemetry → Prometheus (metrics), Jaeger/Tempo (traces) |
| **PACS** | Orthanc or dcm4chee — bought, not built |
| **Healthcare interop** | Firely SDK (FHIR), fo-dicom (DICOM), NHapi (HL7 v2) |
| **Documents** | QuestPDF (PDF), ClosedXML (Excel) |
| **Deployment** | LXC containers on Proxmox, single host |

---

## 3. Backend — ASP.NET Core / .NET 10 / C#

Replaces the NestJS + TypeORM prototype.

**Why:**

- **EF Core change tracking — the decisive factor.** TypeORM has no identity map. A repository returning a mapped domain object cannot know which child rows were added, removed, or edited when it's handed back. Saving an aggregate with collections therefore either silently leaves orphaned rows, requires delete-and-reinsert (which destroys foreign keys and audit trails), or requires hand-written diffing per aggregate. In clinical modules — orders, results, encounters, invoices — that is a patient-safety-grade defect, not a cosmetic one.
- **No mapper tax.** EF Core keeps mapping metadata in a separate configuration file and can bind private fields, so one class serves as both domain entity and persisted entity. TypeORM puts metadata in decorators on the class, which forces a duplicate ORM entity, a bidirectional mapper, and a public rehydration factory that punches a hole in the aggregate's own encapsulation. Roughly 80–150 wasted lines per aggregate across 40 modules, plus three files that must stay in sync.
- **Unit of work.** The DbContext is the transaction scope. Repository ports stay clean; no ORM types leaking into interfaces, no hand-rolled transaction context.
- **Healthcare ecosystem.** Firely, fo-dicom, NHapi. Node has nothing comparable.
- **Active Directory / Kerberos.** Native. Hospital staff expect domain login; Node's story here is duct tape.
- **Documents.** QuestPDF and ClosedXML remove the Puppeteer/Firefox container currently needed for PDF generation.
- **Real threads.** CPU-bound work doesn't need a queue hop to avoid blocking.
- **Supply chain.** ~15 direct NuGet packages vs. ~1,200 transitive npm packages. One vendor, one 3-year LTS cadence.
- **Timing.** Sunk cost is near zero and the port must happen anyway. Writing the destination in C# adds ~10–15% to porting effort; switching after 40 modules costs 100%.

**Costs accepted:** harder hiring than TypeScript; more ceremony per module than NestJS; the team must learn C#, mitigated by its close similarity to TypeScript.

**Rejected:**

| | Why not |
|---|---|
| NestJS + TypeORM | Worst ORM of those considered. The change-tracking gap is disqualifying for clinical aggregates. |
| NestJS + MikroORM | Closes ~80% of the ORM gap (identity map, unit of work, external metadata). Was the fallback for staying in TypeScript. Loses AD, healthcare libraries, threads, document libraries. |
| Python + SQLAlchemy 2.0 | Best ORM story after EF Core. But unenforced type hints across 40 modules and multiple devs is worse than TypeScript, far worse than C#. Weak DI. Only correct if imaging/ML were core. |
| Kotlin / Spring | Best interop ecosystem, excellent hexagonal fit. Slower iteration, worse container story, worst hiring. |
| Go | Best containers, wrong tool for CRUD-heavy multi-module ERP. 2–3× the code per module. |
| Rust | Compile times destroy the ship-fast loop. Hexagonal in async Rust is painful. No local hiring pool. |
| Laravel / Symfony | Fastest ERP module velocity, large Spanish-speaking pool. Weak HL7/DICOM/FHIR. Cultural mismatch. |
| Elixir / Phoenix | Technically elegant, effectively unhireable locally, near-zero healthcare libraries. |

---

## 4. Frontend — Blazor Server now, Blazor WASM for mobile later

The same ASP.NET Core application hosts both the UI and the API.

**Why Blazor Server for desktop:**

- **Lightest possible client.** The browser runs a small JavaScript shim and applies DOM diffs. No framework bundle, no runtime download, no client-side application logic. This satisfies the founding constraint — old hospital PCs — maximally.
- **The WebSocket objection doesn't apply to desktops.** Blazor Server's failure mode is network instability. Hospital desktops are wired, stationary, and sub-millisecond. This is precisely the deployment Blazor Server was designed for.
- **One developer ships a full module.** No API contract negotiation, no DTO duplication, no codegen step, no separate frontend repo. The largest velocity lever available to a small team porting 40 modules.
- **One language, one repo, one hiring pool** across backend and frontend.
- Responsive layout is a non-issue — Blazor emits plain HTML and CSS.

**Why WASM is deferred rather than rejected:** when mobile arrives, Blazor Server is the wrong mode for it. Hospital WiFi roaming resets TCP on every access-point handoff, dropping the circuit and losing component state past the retention window — unacceptable for bedside forms. The answer is per-request render mode selection: desktop routes render interactively on the server, mobile routes render in WebAssembly. Same components, same codebase. This is only possible because of the client isolation boundary in §5.2.

**Costs accepted:** every deploy disconnects every user (single instance, no rolling deploy); RAM per circuit must be monitored; no offline capability — accepted, not wanted.

**Rejected:**

| | Why not |
|---|---|
| Blazor WASM as the primary UI | Heaviest client considered — runtime download plus CPU-costly boot. Directly contradicts the old-PC constraint. Correct for mobile, wrong for a desktop-dominant deployment. |
| Angular | Steep learning curve. Loses shared types once the backend is C#. |
| Vue + Quasar | Strongest JavaScript option — lowest learning curve, huge component set, PWA and mobile from one codebase. Loses one-language and shared contracts. **Retained as the contingency** if Blazor proves unworkable. |
| React + AG Grid | Best grids and hiring pool, best path to React Native. Same losses as Vue, higher ceiling for bad decisions. |
| HTMX + Razor | Near-zero client load, trivial to learn. Strains on scheduling boards, filterable grids, multi-step order entry. |

---

## 5. Application Architecture

### 5.1 Modular monolith

One API process plus one worker process. Microservices would destroy shipping velocity at this team size and stage.

### 5.2 ⭐ Client isolation — the load-bearing constraint

All interactive UI components live in a Razor Class Library that **references only the shared contracts project.** It cannot see the application layer, the infrastructure layer, EF Core, or the DbContext.

Components therefore code against a service interface they declare themselves; the host project supplies the implementation. On the server that implementation calls handlers in-process. In a future WebAssembly host it calls the REST API instead.

**Why this matters more than it looks:** because the client project cannot reference the application layer, shortcuts are *impossible to compile*. No CI rule to maintain, no discipline required, no way for a junior developer to get it wrong. It is also the single thing that makes mobile a week of work rather than a rewrite of 40 modules.

**This must be established before the first module is ported.**

### 5.3 Hexagonal, applied selectively

Full ports and adapters on an HR leave-request CRUD is pure tax.

| Approach | Modules |
|---|---|
| **Full hexagonal** — rich domain, ports, use-case handlers | CPOE/orders, LIS, RIS, EMR/encounters, billing, pharmacy, scheduling, interop |
| **Pragmatic layered** — the EF entity is the model, service layer, no mapper | HR, inventory, ERP config, catalogs, admin, most CRM |

Roughly 8–12 of 40 modules warrant the full treatment: those with real invariants and child collections, where a silent wrong write is a patient-safety event.

### 5.4 Three API contracts, one application layer

```
              ┌─────────────────────────────┐
              │      Application layer      │
              └──┬─────────┬─────────┬──────┘
                 │         │         │
    ┌────────────┘         │         └────────────┐
┌───┴────────────┐ ┌───────┴─────────┐ ┌──────────┴──────┐
│  Internal API  │ │   Public API    │ │  FHIR R4 façade │
│  web + mobile  │ │  3rd parties    │ │ other hospitals │
│  OIDC          │ │  OAuth2         │ │  SMART on FHIR  │
│  changes freely│ │  deprecation    │ │  Firely SDK     │
└────────────────┘ └─────────────────┘ └─────────────────┘
```

One REST API cannot serve both the UI and external hospital interop — the shapes are irreconcilable. Other hospitals expect FHIR, not a bespoke API.

**Build the REST controllers now**, even though Blazor Server doesn't consume them. Third parties and other hospitals need them regardless, and an API nobody consumes rots silently.

### 5.5 Interop — buy, don't build

| Concern | Approach |
|---|---|
| **PACS** | **Do not build.** Deploy Orthanc or dcm4chee. |
| **DICOM viewing** | OHIF / Cornerstone, embedded as a JavaScript island. Radiologists need real workstations regardless; ward PCs need adequate review quality only. |
| **DICOM server-side** | fo-dicom, or delegate to the PACS. |
| **HL7 v2 instrument interfaces** | NHapi in a dedicated service, or Mirth Connect. The work is interfacing, not CRUD. |
| **FHIR** | Firely SDK, as its own adapter. |

Interop is a first-class subsystem with its own adapters. This is where hexagonal genuinely earns its keep.

---

## 6. ⭐ Store Roles — the app knows roles, never topology

The application layer references **roles**, not products, hosts, or connection details. Topology lives entirely in configuration. Moving PostgreSQL to another machine, or swapping RustFS for SeaweedFS, is a config edit — never a code change.

The same binary must run another hospital's everything-on-one-host deployment, our five-LXC split, or a future multi-machine setup, with no code difference.

| Role | Abstraction | Owner | Implementation |
|---|---|---|---|
| **Data** | Repository interface per aggregate | Ours | EF Core + Npgsql → PostgreSQL |
| **Blob** | Object storage port | Ours | AWS S3 SDK → RustFS |
| **Search** | Search index port | Ours | PostgreSQL FTS + Meilisearch, per collection |
| **In-memory** | `IDistributedCache` | **.NET built-in** | Redis |
| **Metrics** | OpenTelemetry `Meter` | **Built-in** | Prometheus exporter |
| **Tracing** | OpenTelemetry `ActivitySource` | **Built-in** | OTLP → Jaeger / Tempo |
| **Logging** | `ILogger<T>` | **Built-in** | Serilog → OTLP |

Four of seven are already standardized by .NET and OpenTelemetry. **Do not invent ports the platform already defines** — the ecosystem plugs into the standard ones.

### 6.1 Do NOT abstract the data store

Engine-agnostic data stores work for key-value systems. We have aggregates, joins, migrations, transactions, and want PostgreSQL-specific features: `jsonb`, arrays, partial indexes, generated columns, schema-per-module, `LISTEN/NOTIFY`. An engine-agnostic layer would cost all of it and buy a portability we will never exercise.

**The data-store abstraction is the repository interface, not the database engine.** Bind concretely to PostgreSQL.

### 6.2 Blob — RustFS

**Why not MinIO:** the community edition is effectively dead. Relicensed from Apache 2.0 to AGPLv3 in 2021; admin and console features stripped from the community build in 2025; community binaries and Docker images no longer published; community development formally ended in early 2026 with engineering focused on the paid AIStor product. AGPLv3 on a commercial hospital product is a separate, real concern.

**Why RustFS:** Apache 2.0 with no copyleft obligations, fully S3-compatible, single binary or container, drop-in migration path from MinIO.

**Known risk:** RustFS is young. Independent 2026 reviews describe it as promising but not yet a default production recommendation, with replication in particular still maturing. This is why the port exists — the adapter uses the standard AWS S3 SDK against a custom endpoint, so **SeaweedFS** (Apache 2.0, mature) or **Ceph RGW** (heavy, bulletproof) are a connection-string change. Avoid Garage — AGPL.

**Validate early:** multipart upload, presigned URLs, and object versioning against the real S3 SDK. "100% S3-compatible" is usually 95%, and the missing 5% is always something you need.

**Never put DICOM studies or scanned documents in the database.** Large binaries in PostgreSQL wreck backup times and poison the buffer cache. This is the reason the blob store exists at all.

### 6.3 Search — split by collection

The search port resolves an adapter **per collection**, because the two backends solve different problems.

| Collection | Backend | Why |
|---|---|---|
| Patients, encounters, clinical notes, orders | **PostgreSQL FTS** | Permission filtering is a join. The index updates in the same transaction as the write, so a patient registered 90 seconds ago is findable *now*. |
| ICD-10, CPT, SNOMED, drug formulary, supply catalog | **Meilisearch** | No permissions to enforce, changes rarely, needs sub-20ms typeahead with real typo tolerance. |

**Why clinical search must not go to Meilisearch:** it would require denormalizing department and role ACLs into every document, re-indexing on every permission change, and accepting that a stalled sync worker makes a real patient invisible while the row sits in the database. That is a clinical safety failure mode, not a UX annoyance.

**Why PostgreSQL FTS is sufficient there:** with the `unaccent` and `pg_trgm` extensions and the Spanish text-search configuration, it handles José/Jose, compound apellidos, and typos. A generated, stored `tsvector` column is maintained by the database itself — no trigger, no sync worker, no lag.

**Why Meilisearch over Elasticsearch:**

| | Meilisearch | Elasticsearch |
|---|---|---|
| License | **MIT** | AGPLv3 / SSPL / ELv2 (triple-licensed since Sept 2024; default distribution is ELv2) |
| Runtime | Single Rust binary | JVM |
| RAM | 200MB–1GB | 2–4GB minimum |
| Typo tolerance | **Native, best-in-class** | Fuzzy query, needs tuning |
| Setup | Config file | Cluster settings, analyzers, mappings, shards |
| Aggregations / log storage | Basic | Far more powerful |

ELv2 prohibits offering the product as a managed service; SSPL requires publishing service-management source. Since HSM may be deployed for other hospitals, MIT sidesteps the question permanently. Elasticsearch's real advantages — analytics and log aggregation — are covered here by SQL and OpenTelemetry respectively. OpenSearch (Apache 2.0) avoids the licensing issue but is still a JVM cluster for a job Meilisearch does better and lighter. Typesense is comparable in quality but GPL-3.0 with a smaller community.

**Meilisearch is a cache, not a system of record.** Source data lives in PostgreSQL; the index is rebuildable. Back up the tables, not the index.

### 6.4 In-memory — Redis

Accessed through the built-in `IDistributedCache` abstraction, so it is one line to swap for in-process memory. Redis also becomes the SignalR backplane the day a second app instance is needed.

### 6.5 Observability — OpenTelemetry

Standard OpenTelemetry primitives used directly, with no custom wrapper interfaces. Metrics export to Prometheus; traces export over OTLP to Jaeger or Tempo. Instrumentation for ASP.NET Core, HttpClient, EF Core, and Npgsql is off-the-shelf.

**Blazor Server–specific signals to watch:** active circuit count, circuit memory, disconnect rate. A circuit leak looks exactly like a memory leak.

---

## 7. Deployment — LXC on a single Proxmox host

One LXC per service, on an internal bridge with no external route. Only the app container (or a reverse proxy in front of it) is reachable from the hospital LAN.

```
┌─ Proxmox host ──────────────────────────────────────────┐
│                                                         │
│  ct-app        ASP.NET Core (Blazor Server + API)       │ ← only exposed service
│  ct-postgres   PostgreSQL   (data + FTS search)         │
│  ct-redis      Redis        (cache; future backplane)   │
│  ct-rustfs     RustFS       (blobs, dedicated dataset)  │
│  ct-meili      Meilisearch  (reference catalogs)        │
│  ct-otel       OTel Collector → Prometheus / Tempo      │
│  (ct-orthanc   PACS, later)                             │
│                                                         │
│  internal bridge — no external route                    │
└─────────────────────────────────────────────────────────┘
```

**Why separate LXCs:** independent resource limits, independent restarts, per-service backup strategies, and a clean path to scaling the app tier later.

### 7.1 Sizing to start

| LXC | RAM | Cores | Disk |
|---|---|---|---|
| ct-app | 4 GB | 4 | 20 GB |
| ct-postgres | 8 GB | 4 | 100 GB + growth |
| ct-redis | 1 GB | 1 | 8 GB |
| ct-rustfs | 2 GB | 2 | dedicated dataset / LV |
| ct-meili | 1 GB | 2 | 16 GB |
| ct-otel | 1 GB | 1 | 16 GB |

PostgreSQL gets the RAM, and must be tuned against the **container's** limits, not the host's.

### 7.2 Known LXC friction

- **Redis and PostgreSQL need host-level kernel settings** that unprivileged containers cannot set — memory overcommit, connection backlog, transparent huge pages. These must be applied on the Proxmox host and persisted.
- **PostgreSQL's shared-memory mount defaults too small in LXC**, which breaks parallel query workers.
- **Running Docker inside an LXC requires nesting.** Running the app as a self-contained binary under systemd avoids the extra layer entirely — decide this per container.
- **RustFS needs a real disk**, bind-mounted rather than living on the container rootfs, so snapshots and replication are meaningful.

### 7.3 Fault isolation is not fault tolerance

Separate LXCs on one Proxmox node isolate failures between services. They do **not** provide redundancy — a power supply or disk failure takes everything down. Acceptable for v1, but the topology must not create a false sense of safety.

### 7.4 Backups

**A filesystem snapshot of a running database is not a database backup.** It is crash-consistent at best.

| LXC | Strategy |
|---|---|
| ct-postgres | Nightly logical dump **plus WAL archiving**, off-box |
| ct-rustfs | Dataset replication off-box |
| ct-meili | None — rebuildable from PostgreSQL |
| ct-redis | None — cache only, disposable |
| ct-app | Stateless — container-level snapshot is fine |

**If backups land on the same Proxmox host, there are no backups.**

### 7.5 Blazor Server operational notes

A single app instance removes Blazor Server's usual burdens — no sticky sessions, no SignalR backplane, no circuit affinity problem. What remains:

- **Every deploy disconnects every user.** No rolling deploy with one instance; releases are off-hours and announced.
- **Any reverse proxy must not kill idle WebSockets** — default read timeouts will drop circuits.
- **Draft autosave is mandatory** for any long clinical form. Circuit loss is not the only way to lose a form; so are dead batteries, closed tabs, and browser crashes.
- **Error boundaries are required** — an unhandled exception kills the whole circuit, not just the component.
- **Escape path if uptime requirements harden:** a second app LXC behind the proxy, with sticky sessions and the Redis backplane. Redis is already deployed, so this is configuration only. Do not build it now.

### 7.6 Certificates

Future PWA installability requires HTTPS with a **trusted** certificate; self-signed will not trigger the install prompt. Either distribute an internal CA via AD/MDM or use real certificates with internal DNS.

---

## 8. Version & Maintenance Policy

- **Target .NET 10** — LTS, supported until 10 November 2028.
- **.NET 8 and .NET 9 both end support 10 November 2026** — do not start on either.
- **Ride LTS only.** Skip .NET 11 (STS, Nov 2026); move to .NET 12 in early 2028. One upgrade every two years with roughly twelve months of overlap. Odd-numbered STS releases are not worth the regression-test burden on a clinical system.
- **Central version management** — target framework and all package versions defined once for the whole solution, with automated dependency PRs.
- **Warnings as errors** — turns future deprecations into build failures rather than silent rot.
- **Main upgrade risk is EF Core**, specifically behavior changes in query translation and migration generation between major versions. Roughly 90% of upgrade friction. Mitigated by integration tests against a real PostgreSQL container.

---

## 9. Sequencing

1. **Establish the client isolation boundary** (§5.2). Everything else depends on getting this right first.
2. Define the store ports and wire OpenTelemetry.
3. Prove one vertical slice end to end — domain, persistence, handler, UI service, Blazor page, REST controller.
4. **Validate RustFS S3 compatibility** before storing anything clinical.
5. **Run a backup and restore drill** to a spare machine, and time it.
6. Port modules.
7. **Mobile, when prioritized:** add the WebAssembly host, route mobile traffic to it, solve auth state across render modes.

---

## 10. Open Items

- **Hospital PC specs** — still never measured. No longer blocking, since Blazor Server is the lightest option available, but it determines headroom for the embedded DICOM viewer, which is WebGL-heavy and the one genuinely demanding client-side workload.
- **RustFS S3 compatibility** — the youngest component in the stack. Verify multipart upload, presigned URLs, and versioning early.
- **Backup and restore drill** — single-host deployment makes this the highest-severity operational gap in the plan.
- **WASM boot benchmark** — deferred with mobile; run before committing to a mobile timeline.
- **Auth across render modes** — the fiddliest part of the future dual-mode design. Prototype before scheduling mobile work.
- **Mobile app scope** — staff use (rounds, orders, results) vs. patient portal. Currently assumed read-mostly staff use. If it becomes write-heavy at the bedside, with wristband and specimen-label scanning, revisit: PWA camera APIs are meaningfully worse than native. The native path would be React Native or Flutter against the API — **not MAUI**.
- **Team growth** — who else writes code over the next five years. The maintainability case for .NET pays off across turnover.

---

## 11. Decision Log

| # | Decision | Rationale | Status |
|---|---|---|---|
| 1 | Backend: ASP.NET Core / .NET 10 / C# | EF Core change tracking; healthcare libraries; AD; threads; timing | ✅ Decided |
| 2 | ORM: EF Core, bound concretely to PostgreSQL | Identity map and unit of work; no mapper duplication | ✅ Decided |
| 3 | Frontend: **Blazor Server** | Lightest client; desktops are wired and stationary; fastest module velocity | ✅ Decided |
| 4 | Client project sees only shared contracts | Compiler-enforced boundary; enables WASM later without rewrite | ✅ Decided — **do first** |
| 5 | Components code against UI service interfaces | Same reason as #4 | ✅ Decided |
| 6 | Blazor WASM for mobile | Server's WebSocket cannot survive WiFi roaming | 📋 Deferred |
| 7 | Per-request render mode selection | Right mode for each network condition | 📋 Deferred |
| 8 | REST controllers built now | Third parties and other hospitals need them; unused APIs rot | ✅ Decided |
| 9 | Fallback frontend: Vue + Quasar | If Blazor proves unworkable | 📋 Contingency |
| 10 | Modular monolith | Microservices would kill velocity | ✅ Decided |
| 11 | Hexagonal, selectively applied | Full treatment only where invariants exist (~8–12 modules) | ✅ Decided |
| 12 | Three API contracts | Internal / public / FHIR — shapes are irreconcilable | ✅ Decided |
| 13 | Store roles, not topology | The app knows an object-storage port, not RustFS; config holds hosts | ✅ Decided |
| 14 | Do NOT abstract the data store | The repository is the port; PostgreSQL-specific features are worth binding to | ✅ Decided |
| 15 | Blob: RustFS behind the S3 SDK | Apache 2.0; MinIO community edition dead and AGPLv3; port keeps it swappable | ✅ Decided |
| 16 | Search split by collection | Permissions and consistency for clinical data; speed and typo tolerance for catalogs | ✅ Decided |
| 17 | Meilisearch over Elasticsearch | MIT vs. AGPL/SSPL/ELv2; lighter; better typeahead | ✅ Decided |
| 18 | Cache: Redis via `IDistributedCache` | Built-in abstraction; also the future SignalR backplane | ✅ Decided |
| 19 | Observability: OpenTelemetry | Standard primitives; no custom wrapper ports | ✅ Decided |
| 20 | Single Proxmox host, one LXC per service | Fault isolation and independent limits — **not** redundancy | ✅ Decided |
| 21 | Off-box backups with WAL archiving | Same-host snapshots are not backups | ✅ Decided |
| 22 | LTS-only version policy | .NET 10 → .NET 12; skip odd-numbered STS | ✅ Decided |
