# Production readiness checklist (implemented)

## 1) Secrets only from environment / secret manager
- `docker-compose.yml` no longer contains insecure `change-me` defaults for DB/RabbitMQ/JWT/Grafana.
- Variables are required via `${VAR:?error}` expansion.
- Use `.env.production` from Vault/KMS or runtime secret injection.

## 3) Strict HTTPS metadata / production mode
- `Jwt__RequireHttpsMetadata=true` enforced in production overrides for `gateway` and `authservice`.
- `ASPNETCORE_ENVIRONMENT=Production` applied for all API services in `docker-compose.prod.yml`.

## 4) Remove permissive CORS from internal services
- `AllowAnyOrigin` removed from:
  - `UserService`
  - `MetricsService`
  - `ReportService`
  - `NotificationService`
  - `AgentManagementService`
- Browser CORS remains controlled at `gateway` only.

## 7) Secret/key rotation policy
- `JWT_ACTIVE_KEY_ID` + dual key slots (`JWT_KEYS_V1`, `JWT_KEYS_V2`) added to runtime config.
- Ops scripts:
  - `scripts/ops/generate_production_secrets.sh`
  - `scripts/ops/rotate_jwt_key.sh`

## 8) Ingress protection + SIEM-ready audit
- Nginx ingress hardened with:
  - request/connection rate limits,
  - stricter auth endpoint throttling,
  - suspicious URI blocking,
  - stricter proxy timeouts and request id forwarding.
- Admin audit events mirrored to structured SIEM log stream (`SIEM_AUDIT`) in `gateway`.

## 9) Blue/Green + Canary + rollback
- `scripts/ops/blue_green_deploy.sh`: blue/green deployment with automatic rollback on failed health check.
- `scripts/ops/agent_canary_rollout.sh`: canary rollout for agents with auto-rollback thresholds.

---

# Database choice: PostgreSQL vs MongoDB

## Current recommendation
Keep PostgreSQL for all current backend services.

## Service-by-service decision
| Service | Primary data | Recommended DB now | MongoDB now? | Notes |
|---|---|---|---|---|
| `AuthService` | users auth, roles, refresh tokens | PostgreSQL | No | transactional auth state and constraints |
| `UserService` | users, computers | PostgreSQL | No | strict relations and referential integrity |
| `ActivityService` | activity events, anomalies | PostgreSQL | No | current model already relational + SQL analytics |
| `MetricsService` | rollups, processed inbox | PostgreSQL | No | idempotency + aggregate queries |
| `ReportService` | daily reports, user stats | PostgreSQL | No | reporting is SQL-first |
| `NotificationService` | notifications, retries, DLQ state | PostgreSQL | No | reliable delivery state machine |
| `AgentManagementService` | agents, policies, commands, DLQ | PostgreSQL | No | control-plane consistency requirements |
| `Gateway` | runtime app settings, RBAC, audit | PostgreSQL | No | admin/audit consistency |

## Why PostgreSQL is the right fit here
- Cross-entity consistency and transactions are heavily used.
- Strong relational model (users/computers/roles/audit/rules/reports) is already implemented.
- Reporting, filtering, and aggregation patterns rely on SQL.
- Operational complexity stays lower with one DB technology family.

## When MongoDB could be justified later
- If high-volume raw activity payloads become semi-structured and schema evolves very frequently.
- If ingestion throughput for append-only event documents outgrows current SQL design.
- If a dedicated event archive is needed with cheap document retention and relaxed consistency.

## Suggested evolution path (without immediate migration)
1. Keep relational source of truth in PostgreSQL.
2. Optionally add a secondary analytical/event store later (e.g., MongoDB or ClickHouse) fed asynchronously.
3. Migrate only specific heavy append-only streams, not identity/auth/reporting/control-plane data.
