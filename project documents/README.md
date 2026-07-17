# Project Documentation — Document Management Platform

This folder is the authoritative project base for the platform (working codename
**Filer**). It captures the vision, requirements, design, and the decisions made
along the way.

---

## How to Read These Docs

Start at `00` for context and `01` for the product vision. `02`–`07` are the
design specifications. `08` is the rule set for AI coding assistants. `09` is the
running decision log — read it to understand *why* things are the way they are.
`10` defines how the codebase is physically structured before implementation
begins. `11` is the Git workflow. `12` is the testing strategy (required tests and
coverage), and `13` is the code-quality and design standard (coding rules,
OOP/SOLID, design patterns, and the Definition of Done) — read `12`/`13` before
writing code. `14` is the roadmap of post-V1 features (intent and sequencing, not
spec) — everything there is deferred until the V1 milestones ship. `15` is the
frontend counterpart of `10`: the client project layout and the conventions the
FE milestones established — read it before touching `src/Clients/`.

---

## Document Index

| Doc | Title                       | Purpose                                              | Status |
|-----|-----------------------------|------------------------------------------------------|--------|
| 00  | Project Context             | Scope, tech direction, principles, modules           | Active |
| 01  | Product Vision              | Vision, objectives, users, workflows                 | Active |
| 02  | Data Model                  | Entities, relationships, PostgreSQL specifics        | Active |
| 03  | API Specification           | REST conventions, auth, endpoints, errors            | Active |
| 04  | Non-Functional Requirements | File limits, performance, observability, retention   | Active |
| 05  | Security Model              | Auth, ownership, upload/file security, secrets       | Active |
| 06  | AI Analysis Pipeline        | Async job lifecycle, provider abstraction, retries   | Active |
| 07  | Storage & Deployment        | Storage abstraction, Docker topology, evolution      | Active |
| 08  | AI Development Guidelines    | Rules for AI coding assistants                       | Active |
| 09  | Decision Log (ADRs)         | Significant decisions and their rationale            | Active |
| 10  | Solution & Architecture Layout | Projects, module structure, dependency rules, slices | Active |
| 11  | Git Workflow                | Branching, commits, PR, CI, branch protection        | Active |
| 12  | Testing Strategy            | Test pyramid, required tests per slice, coverage gate | Active |
| 13  | Code Quality & Design       | Coding standards, OOP/SOLID, patterns, Definition of Done | Active |
| 14  | Roadmap                     | Post-V1 features: intent, rationale, sequencing      | Active |
| 15  | Frontend Architecture       | Client projects, service seams, page patterns, tokens, bUnit idioms | Active |

---

## Resolved Key Decisions

See `09-decision-log.md` for full rationale.

* **ADR-001 — Frontend:** Blazor (Blazor WebAssembly for web; MAUI Blazor Hybrid
  for Windows/Android; shared Razor Class Library).
* **ADR-002 — Database:** PostgreSQL (JSONB for flexible metadata; pgvector
  reserved for future semantic search).
* **ADR-003 — Architecture:** Modular monolith with vertical slices;
  infrastructure behind abstractions; CQRS/DDD/microservices deferred.
* **ADR-004 — Solution layout:** Project-per-module (with `*.Contracts`); plain
  feature services; pragmatic minimal-API-vs-controller choice per slice.

---

## Confirmed Stack

* **Backend:** .NET 10, ASP.NET Core, REST API, ASP.NET Core Identity + JWT.
* **Frontend:** Blazor WebAssembly (web) / MAUI Blazor Hybrid (desktop, mobile).
* **Database:** PostgreSQL (via Npgsql + EF Core).
* **Storage:** Local filesystem (Docker volume) in V1; S3-compatible later.
* **Deployment:** Docker-first.
* **AI:** Async background processing behind `IAIAnalysisProvider`.

---

## Conventions

* Documents are numbered `NN-kebab-case-title.md`; the number defines reading
  order.
* Each design doc lists its related documents and related ADRs at the top.
* Decisions are recorded as ADRs in `09` (append-only; supersede rather than
  delete).
* Open questions are tracked inline at the end of the relevant doc and resolved
  over time.

---

## Open Items (Cross-Document)

Tracked in the relevant docs; highlights:

* Folder nesting depth and single-vs-multi folder membership (`02`).
* Email verification, password reset, login rate limiting (`05`).
* Message broker and orchestration target for the scale-out phase (`06`, `07`).

---
