# `deploy/` — déploiement mono-nœud auto-hébergé

Ce dossier est le **contrat de déploiement** de Filer : ce qu'un hôte doit
fournir, et comment une version y arrive. Il décrit *un* nœud unique, pas une
machine en particulier — aucune adresse, aucun nom d'hôte, aucun identifiant
réel n'a sa place ici (le dépôt est public).

Décision de fond : **ADR-018** (`project documents/09-decision-log.md`).
Topologie et cycle de vie : `07-storage-and-deployment.md`.

| Fichier | Rôle |
|---|---|
| `docker-compose.prod.yml` | La composition de production. Autonome — il *remplace* le compose de dev, il ne le surcharge pas. |
| `.env.example` | Modèle de `/srv/filer/.env`. Secrets et tag d'image. |
| `backup-filer.sh` | Dump PostgreSQL + copie des blobs, dans cet ordre. |
| `choix-runtime-llm.md` | Comparatif Ollama / llama.cpp mesuré, et critère de bascule. |

---

## Ce que l'hôte doit fournir

| | |
|---|---|
| Docker Engine + plugin Compose | dépôt officiel Docker, pas le paquet de la distribution |
| `/srv/filer` **inscriptible par l'utilisateur de déploiement** | le CD y écrit le compose et le pin d'image, sans `sudo` |
| `/srv/pgdata` | données PostgreSQL |
| `/srv/data/filer/blobs` | blobs documents, **`chown 1654:1654`** |
| `/srv/backup` | destination des sauvegardes |
| Ollama joignable depuis les conteneurs | voir ci-dessous — deux réglages, pas un |
| Un chemin d'administration hors LAN | Tailscale ou équivalent ; c'est aussi le chemin du CD |

> ⚠️ **`chown 1654` n'est pas cosmétique.** Le conteneur tourne sous cet UID
> (`USER $APP_UID`) et le module Storage teste que la racine des blobs est
> *inscriptible* : un mauvais propriétaire fait échouer `/health/ready`, donc le
> healthcheck, donc le déploiement — sans message évident côté application.

### Ollama — les deux réglages qui vont ensemble

`extra_hosts: host.docker.internal:host-gateway` fait uniquement de la
**résolution de nom** vers la passerelle Docker. Un service lié à `127.0.0.1`
n'écoute pas dessus : le conteneur prend un `ECONNREFUSED`. Il faut donc **aussi**
lier Ollama à cette passerelle, et **en plus** ouvrir UFW.

```bash
ip -4 addr show docker0 | awk '/inet /{print $2}'   # relever l'IP réelle, ne pas
                                                     # écrire 172.17.0.1 en dur
sudo systemctl edit ollama.service                   # Environment="OLLAMA_HOST=<gw>:11434"

sudo ufw allow from 172.16.0.0/12 to any port 11434 proto tcp
sudo ufw reload
```

> ⚠️ **UFW est le piège coûteux.** Lier Ollama à la passerelle ne suffit pas : la
> politique INPUT par défaut est *deny*, donc le trafic conteneur → hôte:11434
> **timeout** pendant que `curl` depuis l'hôte répond `200`. Le symptôme côté
> Filer est un `AnalysisJob` qui échoue en boucle sur `Connection timed out`,
> avec un diagnostic qui accuse Ollama à tort.
>
> La règle ci-dessus couvre `docker0` *et* le réseau projet créé par Compose.
> Elle n'expose rien au LAN — nettement préférable à `OLLAMA_HOST=0.0.0.0`, qui
> publierait une API **sans authentification** sur tout le réseau local.

---

## Installer

```bash
sudo mkdir -p /srv/filer
sudo chown "$USER" /srv/filer           # le CD y écrit sans sudo
sudo cp docker-compose.prod.yml /srv/filer/   # amorçage seulement — voir plus bas
sudo cp .env.example            /srv/filer/.env
sudo chown "$USER" /srv/filer/.env /srv/filer/docker-compose.prod.yml
sudo chmod 600 /srv/filer/.env          # secrets en clair
```

> Le `cp` du compose n'est qu'un **amorçage**, utile pour un premier démarrage à
> la main. Ensuite le fichier est **écrit par le pipeline à chaque déploiement**,
> depuis le commit dont l'image a été construite : ne pas l'éditer sur le
> serveur, la modification serait écrasée au déploiement suivant — silencieusement
> et sans erreur. Le seul fichier que le CD ne touche jamais est `.env`.

Renseigner `/srv/filer/.env` :

```bash
openssl rand -base64 32     # POSTGRES_PASSWORD
openssl rand -base64 48     # JWT_SIGNING_KEY  (≥ 32 caractères)
```

`FILER_IMAGE_TAG` doit pointer un tag réellement publié. `OLLAMA_MODEL` doit
correspondre à un modèle réellement tiré (`ollama list`).

---

## Déployer

Le chemin normal est le workflow `cd.yml` : pousser un tag `v*` déclenche le
déploiement. Il livre **deux** choses issues du même commit — celui inscrit dans
le label OCI `revision` de l'image :

1. `docker-compose.prod.yml`, écrit sur le nœud depuis ce commit ;
2. le pin `FILER_IMAGE_TAG` dans `.env`.

Puis `pull`, `up -d`, et attente de l'état `healthy` du conteneur.

Manuellement, sur l'hôte — chemin de secours (contrôle Tailscale indisponible,
par exemple) :

```bash
cd /srv/filer
sed -i 's/^FILER_IMAGE_TAG=.*/FILER_IMAGE_TAG=v0.3.1/' .env
docker compose -f docker-compose.prod.yml --env-file .env pull
docker compose -f docker-compose.prod.yml --env-file .env up -d
docker compose -f docker-compose.prod.yml --env-file .env ps      # attendre `healthy`
```

> ⚠️ Ce chemin manuel ne déploie **que l'image** : il tourne avec le compose déjà
> présent. Si la version visée en change un (nouveau service, nouveau digest
> PostgreSQL, nouvelle variable), copier aussi le fichier depuis le dépôt à ce
> commit. C'est précisément l'écart que le pipeline supprime (#274) — d'où la
> préférence pour un `workflow_dispatch` plutôt que ces quatre lignes.

Les migrations EF s'appliquent **au démarrage du conteneur** : il n'y a pas
d'étape de migration séparée. Corollaire — une migration fautive est un
déploiement fautif, et le retour arrière est la seule manœuvre.

> ⚠️ **Toujours `-f docker-compose.prod.yml`.** Un `docker compose up` nu dans le
> dépôt cloné charge automatiquement `docker-compose.override.yml`, un overlay
> Visual Studio/Windows (`${APPDATA}`, HTTPS 8081 sans certificat) qui casse le
> démarrage sous Linux.
>
> ⚠️ **Ne jamais combiner `-f dev.yml -f prod.yml`** : Compose *concatène* les
> listes `ports`, donc le `8080:8080` du fichier de dev ressusciterait à côté du
> `127.0.0.1:8080:8080` — republiant l'API, et PostgreSQL, sur `0.0.0.0`.

### Savoir ce qui tourne, sans ouvrir de session

Après un déploiement réussi, le workflow CD déplace un tag léger **`deployed`**
sur le commit dont l'image déployée a été construite :

```bash
git fetch --tags --force     # --force obligatoire : ce tag bouge, et un fetch
                             # ordinaire refuse de déplacer un tag déjà présent
                             # (« would clobber existing tag ») — le tag local
                             # reste alors figé sur un ancien déploiement, en
                             # silence.
git log -1 deployed          # ce qui tourne
git diff deployed..main      # ce qui est mergé mais pas encore déployé
```

Le commit est lu sur l'**image** (label OCI `revision`), pas sur le déclencheur du
workflow : après un retour arrière, les deux divergent et c'est l'image qui dit
vrai. Le tag est déplacé **après** la vérification de santé — il constate un
déploiement, il n'en provoque jamais. Ce n'est pas un tag de version : il est
forcé à chaque déploiement, et seuls les `v*` annotés sont des releases
(cf. `11-git-workflow.md`).

### Revenir en arrière

C'est pour cela que `FILER_IMAGE_TAG` est figé et jamais `latest`. Relancer
`cd.yml` (`workflow_dispatch`) avec le tag précédent : il restaure l'image **et**
le compose de ce commit, ce qui est le seul retour arrière complet.

À la main, si le pipeline est indisponible :

```bash
sed -i 's/^FILER_IMAGE_TAG=.*/FILER_IMAGE_TAG=<tag précédent>/' /srv/filer/.env
docker compose -f docker-compose.prod.yml --env-file .env up -d
```

Une migration déjà appliquée n'est pas annulée par ce retour arrière. Si la
version fautive a modifié le schéma de façon non rétro-compatible, restaurer un
dump (voir plus bas) est la seule sortie — d'où la sauvegarde *avant* un
déploiement qui porte une migration lourde.

---

## Sauvegarder

```bash
sudo install -m 0755 backup-filer.sh /usr/local/bin/backup-filer
sudo backup-filer
```

**L'ordre est impératif : dump PostgreSQL d'abord, blobs ensuite.** Un document
créé entre les deux laisse un blob orphelin — inoffensif. Dans l'ordre inverse,
le dump référence une `StorageKey` dont le blob n'a pas encore été copié : la
restauration est cassée, et elle est cassée *silencieusement*.

Automatiser avec un timer systemd. Deux points que le script ne peut pas régler
seul :

- **Une sauvegarde jamais restaurée n'est pas une sauvegarde.** Faire une
  restauration réelle au moins une fois, sur une base jetable.
- **Une copie hors machine reste nécessaire.** Trois disques dans le même boîtier
  ne protègent ni du vol, ni de l'incendie, ni d'une alimentation qui les emporte
  ensemble.

---

## Exploitation courante

### Mettre à jour PostgreSQL

L'image PostgreSQL est **épinglée par digest** dans le compose (#272). Ce n'est
pas de la coquetterie : sans digest, un `compose pull` déclenché par un
déploiement applicatif quelconque met à jour la base **et recrée le conteneur**,
au moment précis où l'attention est ailleurs. C'est arrivé le 2026-08-13.

⚠️ Un tag de patch ne suffit pas. Les images officielles sont reconstruites quand
la base Debian reçoit un correctif : `postgres:17` et `postgres:17.11` pointaient
deux images différentes le même jour. Seul le digest fige.

Dependabot surveille ce fichier et propose les montées de version en PR — les
**mineures et correctifs** seulement : les majeures y sont ignorées
(`.github/dependabot.yml`), pour la raison expliquée plus bas. Pour appliquer une
montée mineure :

```bash
sudo backup-filer                                  # 1. sauvegarder AVANT (dump puis blobs)
# 2. merger la PR Dependabot
gh workflow run cd.yml -f tag=sha-<commit du merge> # 3. déployer : le compose part avec
docker exec filer-postgres postgres -V             # 4. vérifier
```

Le nouveau digest arrive **par le déploiement**, comme n'importe quel autre
changement : il n'y a pas de copie manuelle du compose (#274).

### Changer de version MAJEURE de PostgreSQL

> 🔴 **Une majeure n'est pas un changement de digest.** Le répertoire de données
> d'une majeure n'est pas lisible par la suivante. Mesuré sur la 17→18 avant de
> la faire : le conteneur refuse de démarrer — `FATAL: database files are
> incompatible with server` — et boucle en `restarting` sous `restart:
> unless-stopped`. Le healthcheck ne passe jamais `healthy`, donc `filer.api`,
> qui en dépend (`condition: service_healthy`), ne démarre pas non plus : le
> service entier est à terre. Les données, elles, ne sont **pas** détruites.

Deux choses changent en même temps, et il faut les faire dans cet ordre — le
cluster sur le serveur d'abord, le dépôt ensuite. Une compose qui décrit une
18 alors que `/srv/pgdata` est encore en 17 est un piège armé : il se déclenchera
au prochain déploiement applicatif, puisque la compose part avec chaque déploiement
(#274).

**1. Le serveur** (fenêtre de coupure). Avec des données à conserver, c'est un
dump/restore ; le dump se fait avec le client de la version **cible**, pas de la
version en place :

```bash
sudo backup-filer                                                   # dump + blobs
cd /srv/filer
docker compose -f docker-compose.prod.yml --env-file .env stop filer.api
docker run --rm --network filer_default -e PGPASSWORD="$POSTGRES_PASSWORD" \
  postgres:<majeure cible> pg_dump -h postgres -U "$POSTGRES_USER" \
  -d "$POSTGRES_DB" -Fc > /srv/backup/filer/pre-<majeure>.dump
docker compose -f docker-compose.prod.yml --env-file .env down
sudo mv /srv/pgdata /srv/pgdata.<majeure sortante>   # `mv`, pas `rm` : c'est le retour arrière
sudo mkdir /srv/pgdata
```

Si les données sont jetables, tout ce bloc se réduit au `down`, au `mv` et au
`mkdir` — et il faut alors vider aussi `/srv/data/filer/blobs`, dont les fichiers
ne seraient plus référencés par aucune ligne :

```bash
sudo rm -rf /srv/data/filer/blobs && sudo mkdir -p /srv/data/filer/blobs
sudo chown 1654:1654 /srv/data/filer/blobs           # UID du conteneur (cf. compose)
```

**2. Le dépôt puis le déploiement.** Merger la PR qui porte le digest *et* la
disposition des volumes (elle change d'une majeure à l'autre — voir les
commentaires du compose), puis :

```bash
gh workflow run cd.yml -f tag=<le tag déjà dans .env>
docker exec filer-postgres postgres -V               # vérifier la version servie
```

`initdb` crée le cluster neuf, l'API applique ses migrations au démarrage. Avec
un dump à restaurer, l'insérer entre le `up -d postgres` et le reste :

```bash
docker run --rm --network filer_default -i -e PGPASSWORD="$POSTGRES_PASSWORD" \
  postgres:<majeure cible> pg_restore -h postgres -U "$POSTGRES_USER" \
  -d "$POSTGRES_DB" --no-owner < /srv/backup/filer/pre-<majeure>.dump
```

**Retour arrière** : revert de la PR, `mv /srv/pgdata.<majeure sortante>
/srv/pgdata`, redéployer. Supprimer l'ancien répertoire seulement après quelques
jours de fonctionnement sur la nouvelle majeure.

### Ports

Rien n'est publié hors de la boucle locale, et c'est délibéré : **Docker écrit
ses règles iptables en amont d'UFW**, donc un port publié sur `0.0.0.0` est
joignable depuis le LAN *même si UFW le bloque*. Pour exposer l'API, passer par
un reverse proxy sur l'hôte — lui est bien soumis à UFW.

Conséquence pour l'accès distant : l'API n'étant pas publiée sur l'interface du
VPN non plus, la vérification de santé du CD se fait **depuis l'hôte**, à
travers la session SSH :

```bash
curl -fsS http://127.0.0.1:8080/health/ready
```

### Le nœud VPN ne doit pas expirer

Les clés de nœud Tailscale expirent par défaut (180 jours). Sur un serveur sans
écran, l'expiration coupe le CD *et* le chemin d'administration de secours le
même jour, longtemps après qu'on ait oublié le réglage. **Désactiver
l'expiration de clé** sur le nœud serveur (console d'administration), et la
laisser active sur les machines interactives.

### Surveillance

```bash
df -h                       # trois montages = trois saturations possibles
docker system prune -a      # périodiquement
ollama list                 # les modèles s'accumulent — un MoE pèse ~18 Go
free -h                     # les experts MoE occupent ~18 Go de RAM
```

---

## Provisionnement de l'hôte

L'installation du système (BIOS, partitionnement, pilote GPU, réseau, durcissement
SSH) **n'est pas dans ce dépôt** : elle est propre à une machine et relève du
journal de build de cette machine, pas du contrat de déploiement. Ce fichier
définit ce que l'hôte doit fournir — pas comment l'y amener.

Ce journal hors dépôt est découpé en **« stades » numérotés**, et quelques
fichiers de `deploy/` y font référence. Pour un lecteur du dépôt public, la seule
qui compte : le **stade 7** est la mise en service et la calibration du runtime
LLM natif sur l'hôte — son versant décisionnel est documenté ici, dans
`choix-runtime-llm.md`.
