#!/usr/bin/env bash
# Sauvegarde Filer : dump PostgreSQL puis copie des blobs.
#
# ORDRE IMPÉRATIF — dump d'abord, blobs ensuite.
# Un document créé entre les deux laisse un blob orphelin : inoffensif.
# Dans l'ordre inverse, le dump référence une StorageKey dont le blob n'a pas
# encore été copié → restauration cassée.
#
# Installation (script + planification — voir deploy/README.md, « Sauvegarder ») :
#   sudo install -m 0755 backup-filer.sh /usr/local/bin/backup-filer
#   sudo install -m 0644 filer-backup.service filer-backup.timer /etc/systemd/system/
#   sudo systemctl daemon-reload
#   sudo systemctl enable --now filer-backup.timer
#
# Visibilité des échecs : si BACKUP_PING_URL est renseignée dans .env
# (healthchecks.io ou équivalent), le script pingue /start au début, l'URL nue
# en fin de run réussi, /fail sur toute erreur. Le service d'en face alerte
# quand AUCUN ping n'arrive dans la fenêtre attendue — ce qui couvre aussi le
# timer qui ne se déclenche plus, cas qu'aucune alerte locale ne peut voir.

set -euo pipefail

COMPOSE_DIR=/srv/filer
COMPOSE_FILE="$COMPOSE_DIR/docker-compose.prod.yml"
ENV_FILE="$COMPOSE_DIR/.env"
BLOBS_SRC=/srv/data/filer/blobs
DEST=/srv/backup/filer
RETENTION_DAYS=30

DATE=$(date +%Y-%m-%d)
TMP="$DEST/.db-$DATE.sql.gz.partial"

# Lecture clé par clé — jamais `source` : .env est au format Docker Compose, pas
# bash. Une valeur avec espace non citée (`OTLP_HEADERS=Authorization=Basic
# <jeton>`, câblage OpenObserve du 2026-08-19) faisait exécuter le jeton comme
# commande, et `set -e` tuait le script AVANT le ping /start : deux mois sans
# sauvegarde ni ping /fail (#305). Guillemets simples ou doubles retirés, comme
# le fait Compose ; clé absente = valeur vide, à vérifier par l'appelant.
env_get() {
  local v
  v=$({ grep -E "^${1}=" "$ENV_FILE" || true; } | tail -n 1 | cut -d= -f2-)
  v=${v%\"}; v=${v#\"}; v=${v%\'}; v=${v#\'}
  printf '%s' "$v"
}

[ -r "$ENV_FILE" ] || { echo "$ENV_FILE illisible — abandon" >&2; exit 1; }

PING_URL="$(env_get BACKUP_PING_URL)"

# $1 : suffixe optionnel (/start, /fail). Le ping lui-même ne fait jamais
# échouer la sauvegarde : un réseau sortant en panne ne doit pas empêcher le
# dump — le silence côté service de ping suffira à donner l'alerte.
ping_backup() {
  [ -n "$PING_URL" ] || return 0
  curl -fsS -m 10 --retry 3 -o /dev/null "$PING_URL${1:-}" || true
}

mkdir -p "$DEST/blobs"

# Verrou : un run manuel (avant migration, cf. README) ne doit pas s'entrelacer
# avec le run planifié. Pris AVANT le trap — sinon le perdant supprimerait le
# .partial du gagnant en sortant.
exec 9>"$DEST/.lock"
flock -n 9 || { echo "une sauvegarde est déjà en cours — abandon" >&2; exit 1; }

on_exit() {
  status=$?
  rm -f "$TMP"
  [ "$status" -eq 0 ] || ping_backup /fail
}
trap on_exit EXIT

ping_backup /start

# Lues après le /start : une clé manquante doit produire un /fail, pas un
# silence. POSTGRES_PASSWORD n'est pas nécessaire — pg_dump passe par `compose
# exec`, dans le conteneur, qui l'a déjà.
POSTGRES_USER="$(env_get POSTGRES_USER)"
POSTGRES_DB="$(env_get POSTGRES_DB)"
[ -n "$POSTGRES_USER" ] && [ -n "$POSTGRES_DB" ] \
  || { echo "POSTGRES_USER ou POSTGRES_DB absent de $ENV_FILE — abandon" >&2; exit 1; }

compose() {
  docker compose -f "$COMPOSE_FILE" --env-file "$ENV_FILE" "$@"
}

# --- 1. Dump PostgreSQL ------------------------------------------------------
# Le service s'appelle `postgres` (et non `db`). Écriture dans un fichier
# temporaire puis renommage : un dump interrompu ne doit jamais se retrouver
# dans la rotation sous un nom qui le fait passer pour valide.
compose exec -T postgres \
  pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" --clean --if-exists \
  | gzip > "$TMP"

# pipefail fait échouer le script si pg_dump échoue, même si gzip réussit.
[ -s "$TMP" ] || { echo "dump vide — abandon" >&2; exit 1; }
mv "$TMP" "$DEST/db-$DATE.sql.gz"

# --- 2. Blobs ----------------------------------------------------------------
rsync -a --delete "$BLOBS_SRC/" "$DEST/blobs/"

# --- 3. Rétention ------------------------------------------------------------
# Les dumps au-delà de RETENTION_DAYS sont supprimés ; les blobs sont un miroir
# (--delete), donc bornés par la taille de la source. Le bilan ci-dessous part
# dans le journal systemd — c'est lui qui rend la croissance du disque
# observable sans aller compter les fichiers à la main.
find "$DEST" -maxdepth 1 -name 'db-*.sql.gz' -mtime "+$RETENTION_DAYS" -delete

DUMP_COUNT=$(find "$DEST" -maxdepth 1 -name 'db-*.sql.gz' | wc -l)
echo "Sauvegarde terminée : $DEST/db-$DATE.sql.gz"
echo "Dumps conservés : $DUMP_COUNT (rétention ${RETENTION_DAYS} j) — blobs : $(du -sh "$DEST/blobs" | cut -f1)"
echo "Espace libre sur la destination : $(df -h --output=avail "$DEST" | tail -1 | tr -d ' ')"

ping_backup

# ⚠️ /srv/backup est un disque du MÊME boîtier. Cette sauvegarde ne protège ni
# du vol, ni de l'incendie, ni d'une alimentation qui emporte les trois disques.
# Une copie hors machine reste à configurer (#260, OPS-M2).
