# Choix du runtime LLM — Ollama, et bascule éventuelle vers llama.cpp

> Document de décision + brief d'implémentation pour l'**équipe Filer**.
> Complète le stade 7 de [`README.md`](README.md).
> Créé 2026-08-09.

## Décision

**Runtime par défaut : Ollama.** Un moteur alternatif (**llama.cpp `llama-server`**) est
techniquement mieux adapté au modèle retenu (MoE en offload hybride GPU/CPU), mais la
bascule n'est justifiée **que si la mesure le prouve** au calibrage du stade 7.

> ✅ **Mesuré le 2026-08-10 : on reste sur Ollama.** Le comparatif (section suivante) montre
> que llama.cpp génère 36 % plus vite mais que l'avantage bout-en-bout est marginal (~0,4 s/doc)
> pour la sortie courte de Filer. La mesure ne force donc pas la bascule.

> 🔄 **Amendé le 2026-08-13 : l'adaptateur OpenAI-compat est planifié** (#268), séquencé
> **après** les deux correctifs de l'adaptateur Ollama (#253 `think`, #254 `num_ctx`) — ceux-là
> concernent ce qui tourne réellement en production.
>
> Le motif n'est pas le benchmark, qui reste valable : c'est la marge et la portabilité.
> L'écart de 36 % en génération se cumule avec la longueur de sortie (RM-04 chat, variante
> agentique #119, résumés), et un seul adaptateur `/v1/chat/completions` couvre llama.cpp,
> vLLM, TGI, LM Studio **et** Ollama — le moteur devient une affaire de configuration.
>
> ⚠️ **Additif, jamais un remplacement.** Le `/v1` d'Ollama n'a pas de champ `think` :
> y router Ollama réintroduirait silencieusement la régression ×10-30 que corrige #253.
> L'adaptateur natif reste le provider par défaut.

Si bascule il y a, elle passe par un **adaptateur OpenAI-compatible générique** côté Filer
(pas un adaptateur `llama.cpp`-spécifique) — développé par l'équipe Filer. Cet adaptateur
parle indifféremment à llama.cpp, Ollama, vLLM, TGI, LM Studio : Filer cesse d'être verrouillé
sur un runtime, et le choix du moteur devient une affaire de configuration, plus de code.

> **Le moteur le plus prometteur (llama.cpp) se teste sans attendre l'adaptateur.** Mesurer un
> runtime (Q1) et l'intégrer à Filer (Q2) sont deux choses distinctes — voir la section
> « Deux questions à ne pas confondre ». On benchmarke `llama-server` en standalone au `curl`,
> et on ne commande l'adaptateur qu'une fois le gagnant connu.

## Résultats du comparatif mesuré (2026-08-10)

Benchmark réalisé sur le serveur, même modèle `qwen3:30b-a3b` (GGUF partagé), **prompt frais
à chaque appel** (cache désactivé — comme en production, chaque document est unique),
raisonnement désactivé des deux côtés. llama.cpp = image Docker `llama.cpp:server-cuda`,
GGUF réutilisé depuis le blob Ollama, offload `--n-cpu-moe 32` (7,2 Go VRAM).

| Métrique (prompt frais, thinking off) | Ollama | llama.cpp |
|---|---|---|
| Prefill / prompt eval | ~360 tok/s | ~350 tok/s (**égalité**) |
| **Génération** | 37,6 tok/s | **51 tok/s (+36 %)** |
| **Latence / document** (~100 tok JSON) | ~3,2 s | ~3,1 s (**marginal**) |
| VRAM | 6,8 Go | 7,2 Go |
| Qualité classification | ✅ correcte | ✅ correcte |

> ⚠️ **Piège de mesure corrigé.** Les premiers chiffres (Ollama 2,5 s, llama.cpp 2,1 s)
> étaient **gonflés par le cache de prompt** (même prompt répété). Sur prompt frais — le seul
> cas réaliste, chaque doc étant unique — les deux moteurs sont à **~3 s/doc**.

> ⚠️ **Piège de config llama.cpp.** `--n-cpu-moe` met les experts sur CPU : excellent pour la
> génération, mais à offload trop agressif (`--n-cpu-moe 40`, 8 couches sur GPU) le **prefill
> s'effondre** (28 tok/s). Il faut remplir la VRAM (`--n-cpu-moe 32`, ~7,2 Go) pour un prefill
> correct (~350 tok/s). Le raisonnement Qwen3 ne se coupe **pas** par `--reasoning-budget 0`
> avec le GGUF converti par Ollama (template non standard) : passer par `/completion` avec un
> bloc `<think>\n\n</think>` vide pré-rempli, ou fournir le vrai template jinja Qwen3.

**Verdict : llama.cpp génère 36 % plus vite, mais l'avantage bout-en-bout est marginal
(~0,4 s/doc) car la sortie de Filer est courte (~100 tokens).** Notre critère — « basculer si
llama.cpp dépasse *nettement* » — n'est pas rempli. **Recommandation : rester sur Ollama.**

> **Quand rouvrir la question ?** Si l'usage évolue vers des **sorties longues** (variante
> agentique 2-passes #119, résumés, extraction verbeuse) ou de gros contextes, l'écart de 36 %
> en génération se cumulerait et pourrait justifier la bascule + l'adaptateur OpenAI-compat.
> Pour la classification courte actuelle, non.

## Contexte

- **Matériel** : RTX 2080 SUPER 8 Go (Turing, compute 7.5), i7-13700KF, **62 Go RAM**.
- **Modèle retenu** : MoE `qwen3:30b-a3b` (30 B total / ~3 B actifs), repli `gpt-oss:20b`,
  dépannage dense `llama3.2:3b`. Détails et principe *mémoire = total / vitesse = actifs*
  au stade 7 de la checklist.
- **Dépendance actuelle de Filer** : le seul provider réel est l'adaptateur Ollama, qui
  appelle l'**API native** `POST /api/chat` avec un champ **`format` = schéma JSON**
  (sortie structurée contrainte). Il n'existe **aucun** provider OpenAI aujourd'hui.
  Fichiers concernés :
  - `src/Modules/AiAnalysis/Filer.Modules.AiAnalysis/AiAnalysisModule.cs` (switch de sélection)
  - `.../AiAnalysisOptions.cs` (`OllamaOptions`)
  - `.../OllamaAnalysisProvider.cs` (l'adaptateur ; `OllamaAgenticAnalysisProvider.cs` = variante)

> 🔴 **Correctif requis même en restant sur Ollama.** Le benchmark du 2026-08-10 montre que
> `qwen3:30b-a3b` raisonne par défaut (`thinking`) : **2,5 s/doc sans, 30-104 s/doc avec**.
> L'`OllamaChatRequest` actuel n'envoie pas de champ `think` → il faut ajouter `"think": false`
> au corps `/api/chat`. C'est un correctif de l'adaptateur **existant**, indépendant de toute
> bascule de runtime. Idem pour le futur adaptateur OpenAI-compat (voir plus bas).

## Comparatif des moteurs pour ce setup

| Runtime | Apport vs Ollama | Turing 7.5 | Filer sans code | Verdict |
|---|---|---|---|---|
| **Ollama** (actuel) | gestion des modèles, systemd, offload **automatique** | ✅ | ✅ natif | **Défaut** |
| **llama.cpp** (`llama-server`) | offload **au grain fin** : `--n-cpu-moe`, `-ot` — experts en RAM, chemin chaud sur GPU | ✅ | ❌ adaptateur requis | **Recours si mesure décevante** |
| **vLLM** | débit en batch concurrent (GPU datacenter) | ⚠️ déprécié < 8.0 | ❌ | À écarter |
| **TGI / TensorRT-LLM** | idem, orienté datacenter | ⚠️/❌ | ❌ | À écarter |
| **LM Studio / Jan / Kobold** | c'est llama.cpp, en desktop | ✅ | ❌ | Pas un service headless |

> **Le fait central : Ollama = llama.cpp en dessous.** Changer de moteur n'améliore ni le
> modèle, ni la qualité, ni le support GPU (même GGUF, mêmes noyaux CUDA). Le *seul* gain de
> llama.cpp est le **contrôle du placement des experts** — mais pour du MoE sur 8 Go, c'est
> précisément le nerf de la guerre (gain réaliste ~×2 sur le débit). vLLM/TGI, « plus musclés »
> sur le papier, sont **moins bons ici** : conçus pour un modèle résident en VRAM sur GPU
> récent, et l'usage Filer est une file mono-utilisateur où le batch ne sert à rien.

## Pourquoi un adaptateur OpenAI-compatible (et pas llama.cpp-spécifique)

Même effort de développement, portée bien plus large :

- `llama-server`, Ollama (endpoint `/v1`), vLLM, TGI, LM Studio exposent **tous**
  `POST /v1/chat/completions`. Un unique adaptateur les couvre tous.
- La **sortie structurée est préservée** : l'équivalent standard du `format` d'Ollama est
  `response_format: { type: "json_schema", json_schema: {…} }`, que llama-server traduit en
  grammaire GBNF en interne — **même garantie de contrainte**, **même schéma réutilisé**.
- **No-egress préservé** : `BaseUrl` par défaut sur `localhost`/passerelle Docker — le contenu
  des documents ne sort jamais du déploiement, comme aujourd'hui (05-security, 06-pipeline).

---

## Brief d'implémentation — équipe Filer

Adaptateur **`OpenAiCompatibleAnalysisProvider`**, en miroir de `OllamaAnalysisProvider`.
Empreinte : ~1 fichier de provider + 2 petits ajouts. Le schéma JSON de réponse est
**réutilisé tel quel**.

### 1. `AiAnalysisOptions.cs`

- Nouvelle constante : `public const string OpenAiCompatibleProviderName = "OpenAiCompatible";`
- Nouveau bloc d'options calqué sur `OllamaOptions` :

```csharp
public sealed class OpenAiCompatibleOptions
{
    public string BaseUrl { get; init; } = "http://localhost:11434/v1"; // llama-server ou Ollama /v1
    public string Model { get; init; } = "qwen3-30b-a3b";
    public string? ApiKey { get; init; }          // llama-server --api-key ; ignoré par Ollama
    public int TimeoutSeconds { get; init; } = 300;
    public int MaxPromptChars { get; init; } = 8_000;
}
```

- Propriété `OpenAiCompatible` dans `AiAnalysisOptions` (à côté de `Ollama`).

### 2. `AiAnalysisModule.cs`

- Un `case AiAnalysisOptions.OpenAiCompatibleProviderName:` dans le switch (ligne ~45),
  qui enregistre le nouvel adaptateur en typed-HttpClient (mêmes règles `Validate` :
  `BaseUrl` URL absolue, `Model` non vide, `TimeoutSeconds` > 0, `MaxPromptChars` > 0).
- Ajouter le nom au message d'erreur `default:`.
- Injecter l'en-tête `Authorization: Bearer {ApiKey}` **si** `ApiKey` est renseigné.

### 3. `OpenAiCompatibleAnalysisProvider.cs`

Copie de `OllamaAnalysisProvider` avec **trois** changements ; tout le reste
(`BuildPrompt`, `RenderFolderTree`, `MapReply`, le logging, la gestion timeout/erreur)
est identique.

**a. Endpoint** : `POST v1/chat/completions` (au lieu de `api/chat`).

**b. Forme de requête** — `format` → `response_format` :

> 🔴 **Désactiver le raisonnement.** Pour un modèle à raisonnement hybride (Qwen3, gpt-oss),
> l'adaptateur doit couper le `thinking` (sinon ~40× plus lent, cf. benchmark). En
> OpenAI-compat, c'est propre au modèle/moteur : `"chat_template_kwargs": {"enable_thinking": false}`
> (llama-server/vLLM Qwen3), ou `"reasoning_effort": "low"` (gpt-oss), ou pour Ollama via `/v1`
> le champ n'existe pas → préférer l'API native `/api/chat` avec `"think": false`. À valider
> selon le runtime cible.

```jsonc
{
  "model": "qwen3-30b-a3b",
  "messages": [ { "role": "user", "content": "<prompt identique>" } ],
  "stream": false,
  "chat_template_kwargs": { "enable_thinking": false },
  "response_format": {
    "type": "json_schema",
    "json_schema": {
      "name": "document_suggestion",
      "strict": true,
      "schema": { /* LE MÊME schéma que BuildResponseSchema(), voir ci-dessous */ }
    }
  }
}
```

**c. Forme de réponse** : le JSON du modèle est dans `choices[0].message.content`
(une **chaîne** à re-parser), au lieu de `message.content` chez Ollama. Wire types :

```csharp
private sealed record OpenAiChatResponse(
    [property: JsonPropertyName("choices")] IReadOnlyList<OpenAiChoice>? Choices);
private sealed record OpenAiChoice(
    [property: JsonPropertyName("message")] OpenAiChatMessage? Message);
private sealed record OpenAiChatMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content);
```

`MapReply` reste identique une fois `content` récupéré : on désérialise vers
`OllamaSuggestion` (`folder{name,confidence}`, `tags[]{name,confidence}`) — renommer si
souhaité, la structure est la même.

**Schéma réutilisé** (identique à `BuildResponseSchema()` actuel) :

```json
{
  "type": "object",
  "properties": {
    "folder": {
      "type": "object",
      "properties": { "name": { "type": "string" }, "confidence": { "type": "number" } },
      "required": ["name", "confidence"],
      "additionalProperties": false
    },
    "tags": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": { "name": { "type": "string" }, "confidence": { "type": "number" } },
        "required": ["name", "confidence"],
        "additionalProperties": false
      }
    }
  },
  "required": ["folder", "tags"],
  "additionalProperties": false
}
```

> ⚠️ **Mode `strict`.** L'OpenAI-compat en mode strict exige `additionalProperties: false`
> sur chaque objet et toutes les propriétés dans `required` (ajouté ci-dessus vs le schéma
> Ollama actuel). llama-server respecte `response_format`/`json_schema` ; si un runtime cible
> ne gère pas `strict`, retomber sur `{"type":"json_object"}` + validation applicative.

### Tests

Miroir de la suite existante de `OllamaAnalysisProvider` : réponse bien formée → mapping
correct ; JSON invalide/vide → `OllamaReplyUnusable` + rethrow ; statut HTTP non-2xx →
`HttpRequestException` ; timeout → `OperationCanceledException`. Fixer un faux serveur qui
renvoie la forme `choices[0].message.content`.

---

## Côté ops — si l'on passe à llama.cpp

Différences opérationnelles vs Ollama (à intégrer au stade 7 le moment venu) :

- **Pas de gestion de modèles intégrée** : télécharger le GGUF à la main (ex. un
  `Qwen3-30B-A3B` Q4_K_M depuis Hugging Face) dans `/srv/data/models`.
- **Unité systemd `llama-server`** à écrire (Ollama fournissait la sienne). Lier à la
  passerelle Docker (`--host 172.17.0.1 --port 11434`) — même exigence que le stade 7
  pour la joignabilité depuis les conteneurs.
- **Leviers d'offload MoE** (à confirmer selon la version de llama.cpp installée) :

```bash
llama-server -m /srv/data/models/qwen3-30b-a3b-Q4_K_M.gguf \
  --n-gpu-layers 99 \
  --n-cpu-moe N \            # garde les experts de N couches sur le CPU ; calibrer sur nvidia-smi
  -c 8192 -fa \             # contexte + flash attention
  --jinja \                 # gabarit de chat du modèle
  --host 172.17.0.1 --port 11434 \
  --api-key <clef>          # puis Filer: OpenAiCompatible.ApiKey
```

`--n-cpu-moe N` (ou `-ot "\.ffn_(up|down|gate)_exps\.=CPU"`) est **le** réglage à ajuster :
monter les couches sur GPU jusqu'à saturer les ~7 Go/8 sans OOM.

## Deux questions à ne pas confondre

- **Q1 — performance du moteur** : quel runtime donne le meilleur débit pour
  `qwen3:30b-a3b` sur cette carte ? Se mesure **sans aucun code Filer**, sur Ollama
  *comme* sur llama.cpp, au `curl`/`llama-bench`.
- **Q2 — intégration Filer** : brancher le gagnant → l'adaptateur OpenAI-compat.

> ⚠️ **Benchmarker llama.cpp ne nécessite pas l'adaptateur.** On monte `llama-server` en
> standalone et on le tape en direct. L'adaptateur ne sert qu'à l'intégration finale (Q2),
> **pas** au test du moteur (Q1). Le moteur prometteur peut donc — et doit — être mesuré tôt,
> à moindre coût, avant tout engagement de dev.

## Critère de bascule (mesurable, pas au feeling)

Métriques à relever pour chaque moteur, avec un prompt représentatif (contexte
dossiers/tags + ~8 000 car. de texte, sortie JSON contrainte) :

- débit (tok/s, prompt eval + génération) et **latence par document** ;
- split GPU/CPU (`ollama ps` côté Ollama) et `nvidia-smi` (VRAM occupée, utilisation GPU).

**Référence Ollama mesurée (2026-08-10, `qwen3:30b-a3b`, `think:false`)** — le seuil que
llama.cpp devra battre pour justifier la bascule :

- latence **~2,5 s/document** à chaud ; génération **~37 tok/s** ; chargement à froid ~24 s ;
- **VRAM 6,8 / 8 Go**, split **63 % CPU / 37 % GPU**, contexte 4096 ;
- classification correcte.

Décision :

1. **Garder Ollama** si son débit suffit pour la file de jobs — c'est le seul option
   *aussi* gratuite à déployer (déjà intégrée, zéro adaptateur).
2. **Passer à llama.cpp** si son débit le dépasse nettement **et** que le gain vient d'un GPU
   mieux exploité (offload fin des experts) — gain réaliste ~×2 quand Ollama laisse le GPU
   en attente du CPU.
3. **Ne rien changer** si le GPU est déjà saturé au maximum utile : llama.cpp n'y changerait
   presque rien, pour le coût d'un adaptateur + d'une unité systemd à maintenir.

## Séquencement

> Principe : **mesurer le(s) moteur(s) d'abord (Q1, zéro code), n'intégrer que le gagnant (Q2).**
> On n'engage jamais l'équipe Filer sur un adaptateur spéculatif, et on n'abandonne jamais le
> MoE sans avoir testé le moteur censé le sauver.

1. **Ollama** (déjà installé au stade 7), `qwen3:30b-a3b` : mesurer.
   - Suffit largement → **stop**, zéro adaptateur, terminé.
   - Déçoit → étape 2 (ne pas conclure « MoE trop lent » à ce stade).
2. **llama-server standalone** (toujours zéro code Filer) : GGUF manuel, tuner `--n-cpu-moe`,
   benchmarker, comparer à Ollama.
   - *Option directe* : si l'on veut trancher sans détour, faire 1 et 2 d'entrée, en
     tête-à-tête. Surcoût ops modeste, verdict définitif en une session.
3. **Verdict sur les chiffres** (critère ci-dessus).
4. **Intégration du gagnant seulement** : si Ollama gagne → rien à coder. Si llama.cpp gagne →
   l'équipe Filer livre `OpenAiCompatibleAnalysisProvider`, puis on pointe
   `AiAnalysis:Provider=OpenAiCompatible` + `BaseUrl` sur `llama-server`.
5. **Bonus portabilité** : même sans bascule de perf, l'adaptateur OpenAI-compat permet
   d'A/B les moteurs et de sortir du verrou Ollama — décision produit de l'équipe Filer.

## Playbook de migration vers llama.cpp (validé le 2026-08-10)

> Config **testée et fonctionnelle** sur ce serveur. Repartir d'ici le jour de la migration
> évite de re-découvrir les pièges de cette séance. Prérequis : Docker + nvidia-container-toolkit
> (déjà installés, GPU passthrough vérifié), Ollama installé (fournit le GGUF).

**1. Réutiliser le GGUF déjà téléchargé — pas de re-download.** Le blob Ollama *est* un GGUF
standard (magic `GGUF` vérifié). Chemin :

```
/srv/data/models/blobs/sha256-58574f2e94b99fb9e4391408b57e5aeaaaec10f6384e9a699fc2cb43a5c8eabf
```

> Ce hash correspond à `qwen3:30b-a3b` tiré le 2026-08-10 ; le retrouver via
> `ollama show --modelfile qwen3:30b-a3b` ou en listant `blobs/` par taille (le fichier de 18 Go).

**2. Libérer la VRAM avant de lancer** (Ollama et llama.cpp se disputent les 8 Go) :

```bash
sudo systemctl stop ollama            # rendra la VRAM ; redémarrer après le test
```

**3. Lancer llama-server en conteneur** (image officielle, GGUF monté en lecture seule) :

```bash
B=/models/sha256-58574f2e94b9...   # le blob ci-dessus, vu depuis le conteneur
sudo docker run -d --name llama --gpus all \
  -v /srv/data/models/blobs:/models:ro \
  -p 172.17.0.1:11435:8080 \        # passerelle Docker, comme Ollama (stade 7)
  ghcr.io/ggml-org/llama.cpp:server-cuda \
  -m "$B" --host 0.0.0.0 --port 8080 \
  -ngl 99 --n-cpu-moe 32 -c 4096 -fa on
```

**Pièges rencontrés (tous coûteux à retrouver) :**

| Piège | Symptôme | Correctif |
|---|---|---|
| `-fa` sans valeur | `unknown value for --flash-attn` | `-fa on` (l'API prend `on\|off\|auto`) |
| `--n-cpu-moe` trop bas (ex. 28) | `cudaMalloc failed: out of memory` | remonter ; **32 → 7,2 Go** tient sur 8 Go |
| `--n-cpu-moe` trop haut (ex. 40) | prefill effondré (28 tok/s), 4,5 Go VRAM inutilisés | remplir la VRAM (`32`) → prefill ~350 tok/s |
| Raisonnement Qwen3 non coupé | `content` vide, 300-400 tok de `reasoning_content`, `finish_reason: length` | **`--reasoning-budget 0` ne marche pas** avec le GGUF Ollama (template non standard). Utiliser `/completion` avec un bloc `<think>\n\n</think>` vide pré-rempli (voir ci-dessous), ou fournir le vrai template jinja Qwen3 via `--chat-template-file` |

**4. Couper le raisonnement de façon fiable** via `/completion` (prompt Qwen3 formaté main) :

```
<|im_start|>user
{prompt Filer}<|im_end|>
<|im_start|>assistant
<think>

</think>

```

Le bloc `<think></think>` **vide** signale au modèle de ne pas raisonner → il émet directement
le JSON (contraint par `json_schema`). C'est ce qui a donné les 3,1 s/doc mesurés.

**5. Benchmarker honnêtement : prompt FRAIS à chaque appel.** Le cache de prompt fausse tout
(mêmes prompts répétés → ~2,5 s trompeurs). En production chaque document est unique : mettre
`"cache_prompt": false` et varier le prompt (préfixe `[ref <nonce>]`). Chiffre réaliste obtenu
ainsi : **~3,1 s/doc**, prefill ~350 tok/s, génération ~51 tok/s.

**6. Restaurer après le test :**

```bash
sudo docker rm -f llama
sudo systemctl start ollama
```

**7. Côté Filer** : l'adaptateur OpenAI-compatible reste à écrire (brief plus haut). Penser à
couper le raisonnement selon le runtime (`chat_template_kwargs.enable_thinking:false` si le
template le supporte, sinon `/completion` + bloc think vide) et à réutiliser le schéma JSON.

> Les scripts de benchmark de cette séance (`bench-completion.sh`, `bench-ollama-fresh.sh`,
> payload + schéma) ont été jetables ; ce playbook contient tout le nécessaire pour les refaire.

## Renvois

- Stade 7 de [`README.md`](README.md) — install Ollama,
  réglages systemd, calibrage du split MoE, énergie.
- `06-ai-analysis-pipeline.md` (dépôt Filer) — abstraction provider, privacy/no-egress.
