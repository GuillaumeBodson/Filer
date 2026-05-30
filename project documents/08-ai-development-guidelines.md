# /docs/08-ai-development-guidelines.md

# AI Development Guidelines

## Purpose

This document defines the rules and context AI assistants must follow when generating code, architecture suggestions, or implementation details for the project.

It is intended for:

* GitHub Copilot
* Cursor
* Claude Code
* ChatGPT IDE integrations
* Other AI coding assistants

---

# Global Project Context

## Backend

* .NET 10
* REST API

## Frontend

Pending decision:

* Blazor
* Angular

## Database

Current target:

* PostgreSQL

## Deployment

Mandatory:

* Dockerized environment

---

# Architectural State

Architecture is intentionally undecided.

Avoid assuming:

* Clean Architecture
* Vertical Slice
* CQRS
* DDD
* Microservices

Suggestions may propose these patterns but must not enforce them prematurely.

---

# Core Principles

## API First

The API is the central system entry point.

Future clients:

* Web
* Desktop
* Mobile

must consume the API consistently.

---

## Security First

Security is a core concern.

Always consider:

* Authentication
* Authorization
* Ownership validation
* Secure file access
* JWT validation
* File upload validation

---

## Extensibility

The project must evolve from:

* personal application

toward:

* multi-user SaaS platform

without major rewrites.

---

## Infrastructure Abstraction

Avoid direct coupling to infrastructure implementations.

Examples requiring abstraction:

* File storage
* AI providers
* Background jobs
* Authentication providers

---

# Important Abstractions

## File Storage

Use abstraction:

```csharp
IFileStorageProvider
```

Potential implementations:

* Local filesystem
* S3 storage
* Azure Blob Storage

---

## AI Analysis

Use abstraction:

```csharp
IAIAnalysisProvider
```

Potential implementations:

* OpenAI
* Azure OpenAI
* Ollama
* Local LLM

---

# Upload Processing Rules

Document upload must not execute AI analysis synchronously.

Preferred flow:

1. Upload file
2. Persist metadata
3. Queue analysis job
4. Execute background processing
5. Store analysis results

---

# Coding Guidelines

## General

* Prefer readability.
* Avoid unnecessary abstraction.
* Avoid speculative complexity.
* Prefer explicit naming.

---

## API

* Use versioned routes.
* Return typed responses.
* Use DTOs at boundaries.
* Validate requests explicitly.

---

## Persistence

* Keep persistence concerns isolated.
* Avoid leaking ORM concerns into domain logic.

---

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

# Current Recommended Stack

## Backend

* ASP.NET Core
* ASP.NET Identity
* JWT authentication

## Database

* PostgreSQL

## Storage

### V1

* Local filesystem storage

### Future

* S3-compatible object storage

---

# Recommended Evolution Path

## Phase 1

Single deployable application.

## Phase 2

Background workers.

## Phase 3

Storage provider abstraction improvements.

## Phase 4

SaaS evolution.

---

# Recommended Initial Priorities

1. Authentication
2. Upload pipeline
3. File storage abstraction
4. Folder/tag management
5. AI analysis pipeline
6. Search
7. Observability
