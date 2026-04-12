# Ops scripts

## Полный backup всех Postgres сервисов

```bash
bash /Users/cukw/FinalWork/scripts/ops/backup_all_postgres.sh
```

Кастомная папка:

```bash
bash /Users/cukw/FinalWork/scripts/ops/backup_all_postgres.sh /Users/cukw/FinalWork/backups/manual_2026_03_10
```

## Полный restore всех Postgres сервисов

```bash
bash /Users/cukw/FinalWork/scripts/ops/restore_all_postgres.sh /Users/cukw/FinalWork/backups/20260310_120000
```

Важно:
- restore перезаписывает `public` schema в каждой сервисной БД;
- перед restore убедитесь, что стэк поднят и контейнеры Postgres healthy.

## Создать/обновить bootstrap-админа

```bash
bash /Users/cukw/FinalWork/scripts/ops/ensure_bootstrap_admin.sh
```

## Сгенерировать production secrets (для Vault/KMS)

```bash
bash /Users/cukw/FinalWork/scripts/ops/generate_production_secrets.sh /Users/cukw/FinalWork/.env.production.generated
```

## Ротация JWT signing keys

```bash
# 1) Подготовка второй ключевой пары (JWT_KEYS_V2), active пока прежний
bash /Users/cukw/FinalWork/scripts/ops/rotate_jwt_key.sh /Users/cukw/FinalWork/.env.production prepare

# 2) Переключение active key на v2
bash /Users/cukw/FinalWork/scripts/ops/rotate_jwt_key.sh /Users/cukw/FinalWork/.env.production activate

# 3) После TTL старых токенов — вывод v1 из эксплуатации
bash /Users/cukw/FinalWork/scripts/ops/rotate_jwt_key.sh /Users/cukw/FinalWork/.env.production retire_old
```

## Blue/Green деплой с auto-rollback по health-check

```bash
ENV_FILE=/Users/cukw/FinalWork/.env.production \
bash /Users/cukw/FinalWork/scripts/ops/blue_green_deploy.sh
```

Опционально:
- `CLEAN_OLD=true` — выключить предыдущий цвет после промоута;
- `BLUE_HTTP_PORT`, `GREEN_HTTP_PORT` — изменить порты цветов.

## Canary rollout агентов с auto-rollback

```bash
API_BASE=http://localhost \
DESIRED_VERSION=1.2.3 \
ADMIN_USERNAME=admin \
ADMIN_PASSWORD=admin123 \
bash /Users/cukw/FinalWork/scripts/ops/agent_canary_rollout.sh
```
