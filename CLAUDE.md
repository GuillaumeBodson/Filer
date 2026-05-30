# CLAUDE.md

Filer — plateforme de gestion documentaire. Monolithe modulaire API-first
(.NET 10, PostgreSQL). Ce fichier est le point d'entrée ; il route vers la doc
canonique et donne les commandes. **Il ne reformule pas les faits du projet.**

## Doc canonique (la source de vérité)

Tout le *quoi* vit dans `project documents/`. En cas de conflit avec ce fichier,
**le document canonique gagne** — corrige la doc, ne forke pas le fait.

| Pour…                                               | Voir |
|-----------------------------------------------------|------|
| Stack, scope, modules, principes, architecture      | `00-project-context.md` |
| Vision produit et workflows                         | `01-product-vision.md` |
| Entités, relations, persistance                     | `02-data-model.md` |
| Conventions API, endpoints, forme des erreurs       | `03-api-specification.md` |
| Limites fichiers, performance, observabilité        | `04-non-functional.md` |
| Authentification, ownership, sécurité upload        | `05-security.md` |
| Cycle de vie des jobs IA, provider abstrait         | `06-ai-analysis-pipeline.md` |
| Abstraction stockage et déploiement                 | `07-storage-and-deployment.md` |
| Règles comportementales pour assistants IA          | `08-ai-development-guidelines.md` |
| Décisions et leur rationale (ADR)                   | `09-decision-log.md` |
| Layout solution, projets, règles de dépendance      | `10-solution-structure.md` |

## Commandes

```bash
# Postgres en Docker, API depuis l'IDE/CLI
docker compose up -d postgres
dotnet run --project src/Filer.Api          # http://localhost:8080, migrations au démarrage

# Tout en Docker
docker compose up --build

# Tests (les projets de test vivent sous tests/ — cf. 10-solution-structure.md)
dotnet test

# Nouvelle migration EF (le module Auth possède ses migrations)
dotnet ef migrations add <Name> \
  --project src/Modules/Auth/Filer.Modules.Auth \
  --startup-project src/Filer.Api \
  --output-dir Persistence/Migrations \
  --context AuthDbContext
```

Smoke test : `src/Filer.Api/Filer.Api.http` (register → login → me).

## Règles non négociables

Décisions résolues (ADR) — ne pas rouvrir : Blazor (ADR-001), PostgreSQL
(ADR-002), monolithe modulaire + vertical slices (ADR-003), projet-par-module +
services simples (ADR-004). Ne propose **pas** de microservices, CQRS ou DDD
sans besoin concret justifié.

- **Abstractions** : accès stockage uniquement via `IFileStorageProvider` (`07`),
  IA uniquement via `IAIAnalysisProvider` (`06`). Jamais d'implémentation
  d'infra en dur dans le domaine.
- **Upload toujours asynchrone** : persister les métadonnées, mettre en file un
  job de fond, retourner immédiatement. Jamais d'analyse IA synchrone (`06`/`03`).
- **Sécurité non optionnelle** : JWT + check d'ownership sur chaque opération
  protégée. Accès cross-owner → **404, pas 403** (`05`). Valider les uploads
  (allow-list de type, taille, content sniffing — `04`/`05`).
- **Secrets** : jamais en source control. `Jwt__SigningKey` (≥32 car.) via env/
  secret store. La clé de `appsettings.Development.json` est dev-only.

## Conventions de structure

- Projet-par-module sous `src/Modules/<Module>/` ; un module n'expose que son
  projet `*.Contracts`. Dépendances appliquées par le compilateur **et** par
  `Filer.Architecture.Tests` (`10`).
- Vertical slices : endpoint + service + DTOs + validation par feature.
- `Directory.Build.props` (net10.0, nullable, **warnings-as-errors**),
  `Directory.Packages.props` (versions NuGet centralisées), `.editorconfig`
  (namespaces file-scoped, style imposé au build).

Ordre de construction, style de code et priorités : voir `08`.
