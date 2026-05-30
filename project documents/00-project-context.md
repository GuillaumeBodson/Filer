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

Pending decision:

* Blazor
* Angular

### Database

Current candidate:

* PostgreSQL

### Deployment

Mandatory:

* Dockerized environment

---

## Storage Strategy

### Recommended V1 Approach

Metadata:

* PostgreSQL

Binary file storage:

* Local filesystem mounted through Docker volume

Future evolution:

* S3-compatible object storage

---

## Authentication Strategy

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

## Architectural Constraints

The project architecture is intentionally undecided at this stage.

Avoid premature commitment to:

* Clean Architecture
* Vertical Slice Architecture
* Modular Monolith
* Microservices

The architecture decision must follow clearer understanding of:

* Domain complexity
* AI processing requirements
* Multi-user evolution
* Scaling constraints

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

When generating code or architecture suggestions:

* Do not assume final architecture style.
* Preserve extensibility.
* Avoid overengineering.
* Prefer abstractions around infrastructure concerns.
* Keep the system evolvable toward SaaS.
* Maintain API-first design.
* Respect Docker deployment constraints.
* Avoid coupling AI providers to domain logic.
