#!/usr/bin/env bash
set -euo pipefail

COMPOSE_CMD="${COMPOSE_CMD:-docker compose}"
POSTGRES_SERVICE="${POSTGRES_SERVICE:-postgres-auth}"
DB_NAME="${DB_NAME:-auth}"
DB_USER="${DB_USER:-postgres}"

${COMPOSE_CMD} exec -T "${POSTGRES_SERVICE}" psql -v ON_ERROR_STOP=1 -U "${DB_USER}" -d "${DB_NAME}" <<'SQL'
INSERT INTO roles (name, description) VALUES
('admin', 'Администратор системы'),
('user', 'Обычный пользователь'),
('moderator', 'Модератор'),
('auditor', 'Аудитор безопасности')
ON CONFLICT (name) DO NOTHING;

INSERT INTO auth_users (username, password_hash, email, role_id, is_active)
SELECT
    'admin',
    '$2a$11$mXf13ykig4wVVezgIbc7s.sMDKw2XDkIZXpnYeDElZTZcyyNc1CCm',
    'admin@local',
    r.id,
    TRUE
FROM roles r
WHERE r.name = 'admin'
ON CONFLICT (username) DO UPDATE SET
    password_hash = EXCLUDED.password_hash,
    email = EXCLUDED.email,
    role_id = EXCLUDED.role_id,
    is_active = TRUE;
SQL

echo "Bootstrap admin ensured: username=admin password=admin123"
