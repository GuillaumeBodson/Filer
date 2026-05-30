# /docs/08-ai-development-guidelines.md

# AI Development Guidelines

## Purpose

This document defines the rules and behavioral context AI assistants must follow
when generating code, architecture suggestions, or implementation details for the
project.

It is intended for:

* GitHub Copilot
* Cursor
* Claude Code
* ChatGPT IDE integrations
* Other AI coding assistants

---

# Canonical Sources

This document does **not** restate project facts. It references the authoritative
documents and adds behavioral rules on top. When facts and these guidelines
appear to conflict, the canonical document wins — update the guideline, do not
fork the fact.

| For…                                            | See (authoritative) |
|-------------------------------------------------|---------------------|
| Stack, scope, modules, principles, architecture | `00-project-context.md` |
| Product vision and workflows                    | `01-product-vision.md` |
| Entities, relationships, persistence            | `02-data-model.md` |
| API conventions, endpoints, error shape         | `03-api-specification.md` |
| File limits, performance, observability         | `04-non-functional.md` |
| Authentication, ownership, file/upload security | `05-security.md` |
| AI job lifecycle and provider abstraction       | `06-ai-analysis-pipeline.md` |
| Storage abstraction and deployment              | `07-storage-and-deployment.md` |
| Decisions and their rationale (ADRs)            | `09-decision-log.md` |
| Solution layout, projects, dependency rules, slices | `10-solution-structure.md` |

Resolved decisions an assistant must respect: Blazor frontend (ADR-001),
PostgreSQL (ADR-002), modular monolith + vertical slices (ADR-003),
solution layout — project-per-module, plain feature services, pragmatic API
style (ADR-004).

---

# Behavioral Rules

These are additive directives for code generation; the *what* lives in the
canonical docs, the *how to behave* lives here.

## Respect Resolved Decisions

* The architecture style is decided (ADR-003). Do not propose microservices, and
  do not introduce CQRS or DDD without a concrete, justified need.
* The frontend (ADR-001) and database (ADR-002) are decided. Do not reintroduce
  alternatives as open questions.

## Honor the Abstractions

* Access file storage only through `IFileStorageProvider` (`07`).
* Access AI analysis only through `IAIAnalysisProvider` (`06`).
* Keep authentication, background jobs, and persistence behind their abstractions
  too. Never hardcode a concrete infrastructure implementation into domain logic.

## Upload Must Stay Asynchronous

* Document upload must never run AI analysis synchronously. Persist metadata,
  queue a background job, return immediately. Full flow in `06` / `03`.

## Security Is Not Optional

* Enforce JWT validation and ownership checks on every protected operation, per
  `05`. Cross-owner access returns 404, not 403.
* Validate file uploads (type allow-list, size, content sniffing) per `04`/`05`.

---

# Coding Guidelines

## General

* Prefer readability.
* Avoid unnecessary abstraction.
* Avoid speculative complexity.
* Prefer explicit naming.

## API

* Use versioned routes.
* Return typed responses.
* Use DTOs at boundaries.
* Validate requests explicitly.

## Persistence

* Keep persistence concerns isolated.
* Avoid leaking ORM concerns into domain logic.

## Background Jobs

AI analysis must support:

* retries
* async processing
* cancellation
* observability

---

# Things AI Assistants Must Avoid

Do not:

* hardcode storage implementation
* tightly couple AI providers
* assume cloud-only deployment
* assume SaaS-only design
* overengineer V1
* introduce microservices prematurely

---

# Recommended Initial Priorities

Suggested build order (modules defined in `00`):

1. Authentication
2. Upload pipeline
3. File storage abstraction
4. Folder/tag management
5. AI analysis pipeline
6. Search
7. Observability

The phased evolution path (single app → background workers → storage abstraction
improvements → SaaS) is described in `07-storage-and-deployment.md`.

---
