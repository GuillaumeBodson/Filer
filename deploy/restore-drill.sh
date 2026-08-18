#!/usr/bin/env bash
# Exercice de restauration Filer (#259) : restaurer la DERNIÈRE sauvegarde
# planifiée dans un environnement jetable, sans toucher à la production, et
# prouver que chaque document restauré résout vers ses octets.
#
# Ce que l'ordre de sauvegarde protège (dump d'abord, blobs ensuite), cet
# exercice le TESTE : chaque ligne de documents."Documents" doit pointer un
# blob présent ET dont le sha256 égale le ContentHash stocké. Un dump qui se
# restaure proprement avec des blobs manquants ressemble à un succès — c'est
# précisément le faux positif que la vérification par hash élimine.
#
# Usage (sur le nœud, en root) :
#   sudo restore-drill          # monte l'environnement jetable + vérifie
#   sudo restore-drill down     # démonte tout (conteneurs, réseau, copies)
#
# L'environnement jetable est intégralement disjoint de la production :
#   conteneurs filer-drill-{postgres,api}, réseau filer-drill,
#   répertoire /srv/filer-drill, API sur 127.0.0.1:8081.
# La sauvegarde source n'est lue qu'en rsync : jamais modifiée.

set -euo pipefail

COMPOSE_DIR=/srv/filer
COMPOSE_FILE="$COMPOSE_DIR/docker-compose.prod.yml"   # celui DÉPLOYÉ, pas celui du dépôt
ENV_FILE="$COMPOSE_DIR/.env"
BACKUP_DIR=/srv/backup/filer
DRILL_DIR=/srv/filer-drill
NET=filer-drill
PG=filer-drill-postgres
API=filer-drill-api
PORT=8081

[ "$(id -u)" -eq 0 ] || { echo "lancer en root (sudo)" >&2; exit 1; }

down() {
  docker rm -f "$API" "$PG" >/dev/null 2>&1 || true
  docker network rm "$NET" >/dev/null 2>&1 || true
  rm -rf "$DRILL_DIR"
  echo "Environnement d'exercice démonté."
}

if [ "${1:-}" = "down" ]; then down; exit 0; fi

# shellcheck source=/dev/null
set -a; source "$ENV_FILE"; set +a

# --- 0. La sauvegarde sous test ----------------------------------------------
# Le dump le plus récent. L'exercice n'a de valeur que sur une sauvegarde
# PLANIFIÉE et intacte (mtime ~03:30 UTC) — pas une faite pour l'occasion.
DUMP=$(ls -t "$BACKUP_DIR"/db-*.sql.gz 2>/dev/null | head -1) \
  || { echo "aucun dump dans $BACKUP_DIR" >&2; exit 1; }
[ -n "$DUMP" ] || { echo "aucun dump dans $BACKUP_DIR" >&2; exit 1; }
echo "Dump sous test : $DUMP ($(date -r "$DUMP" '+%F %T %Z'))"
echo "— un mtime vers 03:30 UTC = run planifié ; sinon, l'exercice ne prouve pas le timer."

# La même image PostgreSQL que la production, lue dans le compose déployé :
# un digest dupliqué ici finirait par diverger en silence.
PG_IMAGE=$(grep -oE 'postgres:[0-9.]+@sha256:[a-f0-9]+' "$COMPOSE_FILE" | head -1) \
  || { echo "image postgres introuvable dans $COMPOSE_FILE" >&2; exit 1; }

down >/dev/null   # idempotent : repartir propre si un exercice précédent traîne

# --- 1. Copie des blobs sauvegardés ------------------------------------------
# Copie, pas montage direct : l'API jetable pourrait écrire (jobs restaurés,
# upload d'essai) et la sauvegarde doit rester intacte.
mkdir -p "$DRILL_DIR/blobs"
rsync -a "$BACKUP_DIR/blobs/" "$DRILL_DIR/blobs/"
chown -R 1654:1654 "$DRILL_DIR/blobs"   # UID du conteneur — sinon /health/ready échoue

# --- 2. PostgreSQL jetable + restauration (chronométrée) ---------------------
docker network create "$NET" >/dev/null
docker run -d --name "$PG" --network "$NET" \
  -e POSTGRES_DB="$POSTGRES_DB" -e POSTGRES_USER="$POSTGRES_USER" \
  -e POSTGRES_PASSWORD="$POSTGRES_PASSWORD" \
  "$PG_IMAGE" >/dev/null

for _ in $(seq 1 30); do
  docker exec "$PG" pg_isready -U "$POSTGRES_USER" -d "$POSTGRES_DB" >/dev/null 2>&1 && break
  sleep 2
done
docker exec "$PG" pg_isready -U "$POSTGRES_USER" -d "$POSTGRES_DB" >/dev/null 2>&1 \
  || { echo "PostgreSQL jetable ne répond pas" >&2; exit 1; }

T0=$SECONDS
gunzip -c "$DUMP" | docker exec -i "$PG" \
  psql -q -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" >/dev/null
RESTORE_S=$((SECONDS - T0))
echo "Restauration du dump : ${RESTORE_S}s"

# --- 3. L'API contre la base restaurée ---------------------------------------
# Mêmes réglages que le compose de production, base et blobs jetables, port
# décalé sur la boucle locale. Les migrations s'appliquent au démarrage : sur
# un dump de la même version, un no-op — et le healthcheck le prouve.
docker run -d --name "$API" --network "$NET" \
  -p 127.0.0.1:$PORT:8080 \
  --add-host host.docker.internal:host-gateway \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e ASPNETCORE_HTTP_PORTS=8080 \
  -e ConnectionStrings__Postgres="Host=$PG;Port=5432;Database=$POSTGRES_DB;Username=$POSTGRES_USER;Password=$POSTGRES_PASSWORD" \
  -e Jwt__Issuer=filer-api -e Jwt__Audience=filer-clients \
  -e Jwt__SigningKey="$JWT_SIGNING_KEY" -e Jwt__AccessTokenMinutes=15 \
  -e Storage__Provider=Local -e Storage__RootPath=/data/storage \
  -e AiAnalysis__Provider=Ollama \
  -e AiAnalysis__Ollama__BaseUrl=http://host.docker.internal:11434 \
  -e AiAnalysis__Ollama__Model="${OLLAMA_MODEL:-llama3.2:3b}" \
  -e AiAnalysis__Ollama__TimeoutSeconds=300 \
  -v "$DRILL_DIR/blobs:/data/storage" \
  "${FILER_IMAGE:-ghcr.io/guillaumebodson/filer-api}:$FILER_IMAGE_TAG" >/dev/null

T0=$SECONDS
DEADLINE=$((SECONDS + 180))
until curl -fsS "http://127.0.0.1:$PORT/health/ready" >/dev/null 2>&1; do
  [ $SECONDS -lt $DEADLINE ] || { echo "API pas healthy en 180s — docker logs $API" >&2; exit 1; }
  sleep 3
done
HEALTHY_S=$((SECONDS - T0))
echo "API healthy contre la base restaurée : ${HEALTHY_S}s"

# --- 4. Chaque document doit résoudre vers ses octets -------------------------
# Lignes vivantes uniquement : les soft-supprimées peuvent légitimement attendre
# la purge. Disposition des blobs : {racine}/ab/cd/<clé 64 hex> (module Storage).
TOTAL=0; BAD=0
while IFS='|' read -r key hash; do
  [ -n "$key" ] || continue
  TOTAL=$((TOTAL + 1))
  path="$DRILL_DIR/blobs/${key:0:2}/${key:2:2}/$key"
  if [ ! -f "$path" ]; then
    echo "  MANQUANT : $key" >&2; BAD=$((BAD + 1))
  elif [ "$(sha256sum "$path" | cut -d' ' -f1)" != "$hash" ]; then
    echo "  HASH DIFFÉRENT : $key" >&2; BAD=$((BAD + 1))
  fi
done < <(docker exec "$PG" psql -At -U "$POSTGRES_USER" -d "$POSTGRES_DB" \
  -c 'SELECT "StorageKey", "ContentHash" FROM documents."Documents" WHERE "DeletedAt" IS NULL')

if [ "$BAD" -ne 0 ]; then
  echo "ÉCHEC : $BAD document(s) sur $TOTAL sans octets valides — la sauvegarde ne restaure PAS." >&2
  exit 1
fi

# Zéro ligne = requête en échec ou base vide : dans les deux cas l'exercice n'a
# rien prouvé — ne surtout pas laisser ce cas ressembler à un succès.
if [ "$TOTAL" -eq 0 ]; then
  echo "ÉCHEC : aucun document vivant dans la base restaurée — rien n'a été vérifié." >&2
  exit 1
fi

echo
echo "OK : $TOTAL document(s) restauré(s), chaque blob présent et conforme à son ContentHash."
echo "Durées à consigner dans deploy/README.md : restauration ${RESTORE_S}s, API healthy ${HEALTHY_S}s."
echo
echo "Dernière vérification, HUMAINE — un vrai téléchargement via l'API restaurée :"
echo "  TOKEN=\$(curl -s http://127.0.0.1:$PORT/api/v1/auth/login -H 'Content-Type: application/json' \\"
echo "    -d '{\"email\":\"<votre email>\",\"password\":\"<votre mot de passe>\"}' | jq -r .accessToken)"
echo "  curl -s -H \"Authorization: Bearer \$TOKEN\" http://127.0.0.1:$PORT/api/v1/documents | jq"
echo "  curl -s -H \"Authorization: Bearer \$TOKEN\" -o /tmp/drill-doc \\"
echo "    http://127.0.0.1:$PORT/api/v1/documents/<id>/content"
echo "  sha256sum /tmp/drill-doc   # doit égaler le contentHash de GET /api/v1/documents/<id>"
echo
echo "Puis démonter : sudo restore-drill down"
