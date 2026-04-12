#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT_DIR"

timestamp="$(date +"%Y%m%d_%H%M%S")"
output_dir="${1:-$ROOT_DIR/backups/$timestamp}"
mkdir -p "$output_dir"

declare -a db_map=(
  "postgres-activity:activities"
  "postgres-auth:auth"
  "postgres-user:users"
  "postgres-metrics:metrics"
  "postgres-notification:notifications"
  "postgres-report:reports"
  "postgres-agent:agents"
)

echo "[backup] output: $output_dir"

for pair in "${db_map[@]}"; do
  service="${pair%%:*}"
  database="${pair##*:}"
  out_file="$output_dir/${database}.sql.gz"
  echo "[backup] ${service}/${database} -> ${out_file}"
  docker compose exec -T "$service" pg_dump -U postgres -d "$database" --no-owner --no-privileges | gzip -9 > "$out_file"
done

cat > "$output_dir/manifest.txt" <<EOF
created_at=$timestamp
compose_project_dir=$ROOT_DIR
files=$(ls -1 "$output_dir" | tr '\n' ' ')
EOF

echo "[backup] done"
