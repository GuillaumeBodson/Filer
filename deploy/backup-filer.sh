#!/usr/bin/env bash
# Sauvegarde Filer : dump PostgreSQL puis copie des blobs.
#
# ORDRE IMPÉRATIF — dump d'abord, blobs ensuite.
# Un document créé entre les deux laisse un blob orphelin : inoffensif.
# Dans l'ordre inverse, le dump référence une StorageKey dont le blob n'a pas
# encore été copié → restauration cassée.
#
# Installation :
#   sudo install -m 0755 backup-filer.sh /usr/local/bin/backup-filer
#
# La planification (unité filer-backup.timer) n'existe pas encore : c'est #258
# (OPS-M2). D'ici là, ce script se lance à la main — avant chaque déploiement
# qui porte une migration, au minimum (deploy/README.md).

set -euo pipefail

COMPOSE_DIR=/srv/filer
COMPOSE_FILE="$COMPOSE_DIR/docker-compose.prod.yml"
ENV_FILE="$COMPOSE_DIR/.env"
BLOBS_SRC=/srv/data/filer/blobs
DEST=/srv/backup/filer
RETENTION_DAYS=30

DATE=$(date +%Y-%m-%d)

# shellcheck source=/dev/null
set -a; source "$ENV_FILE"; set +a

mkdir -p "$DEST/blobs"

compose() {
  docker compose -f "$COMPOSE_FILE" --env-file "$ENV_FILE" "$@"
}

# --- 1. Dump PostgreSQL ------------------------------------------------------
# Le service s'appelle `postgres` (et non `db`). Écriture dans un fichier
# temporaire puis renommage : un dump interrompu ne doit jamais se retrouver
# dans la rotation sous un nom qui le fait passer pour valide.
TMP="$DEST/.db-$DATE.sql.gz.partial"
trap 'rm -f "$TMP"' EXIT

compose exec -T postgres \
  pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" --clean --if-exists \
  | gzip > "$TMP"

# pipefail fait échouer le script si pg_dump échoue, même si gzip réussit.
[ -s "$TMP" ] || { echo "dump vide — abandon" >&2; exit 1; }
mv "$TMP" "$DEST/db-$DATE.sql.gz"
trap - EXIT

# --- 2. Blobs ----------------------------------------------------------------
rsync -a --delete "$BLOBS_SRC/" "$DEST/blobs/"

# --- 3. Rétention ------------------------------------------------------------
find "$DEST" -maxdepth 1 -name 'db-*.sql.gz' -mtime "+$RETENTION_DAYS" -delete

echo "Sauvegarde terminée : $DEST/db-$DATE.sql.gz + $(du -sh "$DEST/blobs" | cut -f1) de blobs"

# ⚠️ /srv/backup est un disque du MÊME boîtier. Cette sauvegarde ne protège ni
# du vol, ni de l'incendie, ni d'une alimentation qui emporte les trois disques.
# Une copie hors machine reste à configurer (#260, OPS-M2).
