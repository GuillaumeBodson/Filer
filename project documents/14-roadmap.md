# /docs/14-roadmap.md

# Roadmap — Post-V1 Features

## Purpose

The single source of truth for **features deferred beyond V1**: what we intend to
build, why it is sequenced after the V1 milestones, and how it fits the resolved
architecture. This doc holds *intent and rationale* only — it is **not** a spec
and **not** a decision.

When a feature is committed to development its truth migrates out of here: the
*how* becomes an ADR in `09`, the spec is fleshed into the relevant topic docs
(`02`/`03`/`05`/`06`/…), and the work becomes slice tickets in `backlog/`. The
roadmap entry then shrinks to a stub pointing at the now-canonical detail. This
keeps a single home per fact (`README`, `CLAUDE.md`).

Scope note: this doc covers post-V1 **product features**. Infrastructure
*evolution* of existing V1 abstractions — object storage behind
`IFileStorageProvider`, scale-out topology, the analysis message broker — is
tracked where those abstractions live (`07`, `06`) and is not duplicated here.

Related documents: `00-project-context.md` (Future Scope), `01-product-vision.md`
(Long-Term Vision), `06-ai-analysis-pipeline.md`, `09-decision-log.md`.

---

## Lifecycle

Each entry carries a **Status**:

| Status        | Meaning |
|---------------|---------|
| `Deferred`    | Agreed direction, intentionally sequenced after V1. Not started. |
| `Planned`     | Committed; ADR written and topic docs updated; tickets being cut. |
| `In Progress` | Tickets in flight under a milestone. |
| `Shipped`     | Delivered; entry kept as a historical pointer to the canonical docs. |

A roadmap entry is promoted past `Deferred` only once V1 milestones (M1–M7, see
`backlog/`) are delivered, unless explicitly re-prioritised.

---

## At a glance

| ID    | Feature                          | Status   | Depends on |
|-------|----------------------------------|----------|------------|
| RM-01 | OCR / text extraction            | Deferred | V1 upload + analysis pipeline |
| RM-02 | Document capture by photo        | Deferred | RM-01; MAUI mobile client |
| RM-03 | Email ingestion                  | Deferred | V1 upload pipeline; secrets handling (`05`) |
| RM-04 | AI chat over documents           | Deferred | RM-01; semantic search (embeddings + pgvector) |

Recommended sequence: **RM-01 → RM-02 → RM-03 → RM-04**. OCR is the enabler that
lifts analysis quality and unblocks photo capture; chat sits last because it
needs both good extracted text (RM-01) and the semantic-search layer.

---

## RM-01 — OCR / text extraction

* **Status:** Deferred (post-V1)
* **Problem / value:** Scanned PDFs and images carry no machine-readable text, so
  folder/tag suggestions and duplicate detection degrade. OCR extracts text up
  front, raising the quality of every downstream AI capability.
* **Why deferred:** "OCR-heavy processing" is an explicit **V1 non-goal** (`01`).
  This is a sequencing decision, not a rejection — the V1 non-goal line stays
  true and points here.
* **Architectural fit:** A pre-analysis text-extraction step feeding the existing
  async pipeline (extract → `AnalyzeAsync`), behind its own abstraction
  (e.g. `ITextExtractionProvider`) mirroring `IAIAnalysisProvider` (`06`). No
  change to the job lifecycle. Favour a local/no-egress engine to preserve the
  privacy-first stance (`05`); candidates: Tesseract (local), Azure Document
  Intelligence, AWS Textract.
* **When promoted, touches:** `06` (pipeline step), `04` (added processing
  cost/limits), `00`/`01` (lift the non-goal), ADR in `09` (extraction provider
  abstraction).
* **Decision needed first:** extraction-provider abstraction + default engine → ADR.

---

## RM-02 — Document capture by photo

* **Status:** Deferred (post-V1)
* **Problem / value:** Let a user photograph a paper document on mobile and have
  it stored and analysed like any upload — the fastest capture path for physical
  records.
* **Why deferred:** Low-value without RM-01 (a photo is unanalysable pixels until
  OCR runs) and depends on the MAUI mobile client existing.
* **Architectural fit:** Mostly a **client + ingest** concern. A photo is just
  another upload through the existing async pipeline; camera capture is native to
  the planned MAUI Blazor Hybrid mobile shell (`00`). Backend work is
  image-specific upload validation (allow-list + content sniffing, `05`) and
  optional light preprocessing (deskew/crop) before OCR.
* **When promoted, touches:** client (MAUI), `05` (image upload validation),
  `04` (image size limits); reuses RM-01 for text.
* **Decision needed first:** none architectural — depends on RM-01 landing.

---

## RM-03 — Email ingestion

* **Status:** Deferred (post-V1)
* **Problem / value:** Connect a mailbox so incoming mail and its attachments are
  captured automatically — high-value, hands-off document capture.
* **Why deferred:** Largest net-new surface of the four; needs the V1 upload
  pipeline stable and introduces a new ingestion channel with its own failure
  modes.
* **Architectural fit:** A new **Email Ingestion** module/worker (`10`) that
  authenticates to a mailbox and turns each message into N uploads (body +
  attachments) flowing through the existing async pipeline (`06`). Hard parts:
  mailbox-credential storage (secrets, `05`), mapping an inbox to an owner,
  dedup of re-fetched mail, and untrusted attachments (content-sniffing/allow-list
  matters most here, `05`). Scope an MVP: one connection method, one mailbox per
  user.
* **When promoted, touches:** `10` (new module), `03` (connection endpoints),
  `05` (credentials + untrusted attachments), `02` (possible ingestion-tracking
  state), ADR in `09`.
* **Decision needed first:** connection method (IMAP vs provider inbound webhook
  such as SendGrid / Microsoft Graph) → ADR.

---

## RM-04 — AI chat over documents

* **Status:** Deferred (post-V1)
* **Problem / value:** Natural-language querying and conversation over a user's
  stored documents ("what did my insurance renewal say?"). The payoff layer on
  top of the AI investment.
* **Why deferred:** Depends on two prerequisites — good extracted text (RM-01)
  and a **semantic-search layer** (embeddings + pgvector, already reserved by
  ADR-002 / `02`). Building chat before these is building on sand.
* **Architectural fit:** Layers on the same `IAIAnalysisProvider` abstraction
  (`06`). Prerequisite step — semantic search: generate embeddings into pgvector
  and expose retrieval; chat is then retrieval-augmented generation over that
  index. Both are listed as long-term capabilities in `01`/`06`.
* **When promoted, touches:** `02` (embeddings storage / pgvector), `03` (search +
  chat endpoints), `06` (retrieval + generation flow), `05` (answers must respect
  ownership — never leak cross-owner content), ADR(s) in `09`.
* **Decision needed first:** semantic-search design (embedding model, chunking,
  pgvector schema) → ADR, ahead of any chat work.

---
