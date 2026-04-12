#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT_DIR"

if [[ $# -lt 1 ]]; then
  echo "Usage: $0 <backup_dir>" >&2
  exit 1
fi

backup_dir="$1"
if [[ ! -d "$backup_dir" ]]; then
  echo "[restore] backup dir not found: $backup_dir" >&2
  exit 1
fi

declare -a db_map=(
  "postgres-activity:activities"
  "postgres-auth:auth"
  "postgres-user:users"
  "postgres-metrics:metrics"
  "postgres-notification:notifications"
  "postgres-report:reports"
  "postgres-agent:agents"
)

echo "[restore] source: $backup_dir"
echo "[restore] WARNING: current data in target databases will be replaced."

for pair in "${db_map[@]}"; do
  service="${pair%%:*}"
  database="${pair##*:}"

  gz_file="$backup_dir/${database}.sql.gz"
  sql_file="$backup_dir/${database}.sql"
  if [[ -f "$gz_file" ]]; then
    source_file="$gz_file"
  elif [[ -f "$sql_file" ]]; then
    source_file="$sql_file"
  else
    echo "[restore] skip ${database}: dump file not found (.sql or .sql.gz)" >&2
    continue
  fi

  echo "[restore] ${service}/${database} <- ${source_file}"
  docker compose exec -T "$service" psql -U postgres -d "$database" -v ON_ERROR_STOP=1 \
    -c "DROP SCHEMA IF EXISTS public CASCADE; CREATE SCHEMA public;"

  if [[ "$source_file" == *.gz ]]; then
    gzip -dc "$source_file" | docker compose exec -T "$service" psql -U postgres -d "$database" -v ON_ERROR_STOP=1
  else
    cat "$source_file" | docker compose exec -T "$service" psql -U postgres -d "$database" -v ON_ERROR_STOP=1
  fi
done

echo "[restore] done"
