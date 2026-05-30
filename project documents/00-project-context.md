# /docs/00-project-context.md

# Project Context

## Project Name

Document Management Platform

---

## Project Overview

The project is a centralized document management platform exposing its capabilities through a secured API-first architecture.

The platform allows authenticated users to manage documents through web and future multi-platform clients.

The initial version targets personal usage but must evolve cleanly toward a multi-user SaaS architecture.

---

## Strategic Goals

* Centralize document management.
* Provide secure document access.
* Support extensible client applications.
* Use AI-assisted document analysis.
* Enable scalable future evolution.

---

## Current Scope

### V1 Features

Authenticated users can:

* Visualize owned documents.
* Upload documents.
* Download documents.
* Organize documents using folders.
* Apply tags to documents.

AI-assisted upload processing:

* Automatic document analysis.
* Folder suggestion.
* Tag suggestion.
* Duplicate detection.

---

## Future Scope

Potential future features:

* Multi-user SaaS support.
* Desktop application.
* Mobile application.
* OCR pipelines.
* Semantic search.
* AI chat over documents.
* Sharing and collaboration.
* Role-based permissions.

---

## Technical Direction

### Backend

* .NET 10
* REST API

### Frontend

Decided (2026-05-30): **Blazor**

* Web: Blazor WebAssembly (consumes the REST API).
* Desktop (Windows) and mobile (Android): MAUI Blazor Hybrid, reusing the same UI.
* Shared UI components live in a Razor Class Library reused across web and native shells.
* Blazor Server is explicitly avoided as the foundation (persistent connection does not suit the multi-client / Hybrid target).

See the decision log for full rationale.

### Database

Decided (2026-05-30): **PostgreSQL**

* Relational metadata store (users, documents, folders, tags, ownership).
* JSONB for flexible metadata and AI analysis results.
* pgvector extension reserved for future semantic search / embeddings.
* Built-in full-text search for V1.

See `09-decision-log.md` (ADR-002) for rationale.

### Deployment

Mandatory:

* Dockerized environment

---

## Storage Strategy

Summary; full detail and the `IFileStorageProvider` abstraction live in
`07-storage-and-deployment.md`.

Metadata:

* PostgreSQL

Binary file storage:

* Local filesystem mounted through Docker volume

Future evolution:

* S3-compatible object storage

---

## Authentication Strategy

Summary; the full security model (tokens, ownership, file access) lives in
`05-security.md`.

### V1

* ASP.NET Core Identity
* JWT authentication
* Email/password authentication

### Future

Potential migration toward:

* OpenID Connect
* OAuth2
* External Identity Provider

---

## AI Integration Strategy

Summary; the full job lifecycle and `IAIAnalysisProvider` abstraction live in
`06-ai-analysis-pipeline.md`.

AI processing must be isolated from the main API flow.

Recommended architecture:

* Async processing
* Background worker
* Provider abstraction

Main abstraction:

```csharp
IAIAnalysisProvider
```

Potential future providers:

* OpenAI
* Azure OpenAI
* Ollama
* Local LLMs

---

## Architecture

Decided (2026-05-30): **Modular monolith with vertical slices**

* Topology: a single deployable modular monolith. Modules (Auth, Document
  Management, Folder/Tag Management, AI Analysis, Storage, Search, Background
  Jobs) have explicit boundaries and communicate through interfaces.
* Internal organization: vertical slices (organize by feature, not technical layer).
* Infrastructure (storage, AI providers, persistence) stays behind abstractions
  such as `IFileStorageProvider` and `IAIAnalysisProvider`.
* CQRS, DDD, and microservices are deferred until a concrete need appears.

Module seams preserve the future SaaS / service-extraction path without paying
distributed-systems costs in V1.

See `09-decision-log.md` (ADR-003) for rationale.

---

## Core Design Principles

* API-first
* Security-first
* Extensibility
* Maintainability
* Async processing support
* Docker-first deployment
* Infrastructure abstraction
* AI provider abstraction

---

## Initial Recommended Modules

* Authentication
* Document Management
* Folder Management
* Tag Management
* AI Analysis
* Storage
* Search
* Background Jobs

---

## AI Assistant Instructions

Rules for AI coding assistants are maintained in `08-ai-development-guidelines.md`
(the single source of truth for assistant behavior). They build on the principles
above — preserve extensibility, prefer infrastructure abstractions, keep the
system evolvable toward SaaS, maintain API-first design, and respect the resolved
architecture (ADR-003).
