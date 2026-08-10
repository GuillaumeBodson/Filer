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
sudo cp docker-compose.prod.yml /srv/filer/
sudo cp .env.example            /srv/filer/.env
sudo chmod 600 /srv/filer/.env          # secrets en clair
```

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
déploiement. Manuellement, sur l'hôte :

```bash
cd /srv/filer
sed -i 's/^FILER_IMAGE_TAG=.*/FILER_IMAGE_TAG=v0.3.1/' .env
docker compose -f docker-compose.prod.yml --env-file .env pull
docker compose -f docker-compose.prod.yml --env-file .env up -d
docker compose -f docker-compose.prod.yml --env-file .env ps      # attendre `healthy`
```

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

### Revenir en arrière

C'est pour cela que `FILER_IMAGE_TAG` est figé et jamais `latest` :

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
