# E2E Smoke Tests

This repository includes a smoke E2E script:

- `/Users/cukw/FinalWork/scripts/e2e_smoke.sh`

## What it verifies

1. Auth register + login
2. App settings CRUD persistence (`PUT /api/app-settings` + `GET /api/app-settings`)
3. User creation with mandatory linked computer (1:1 policy)
4. User deletion path
5. Agent block/unblock command endpoints and command history (if at least one agent exists)
6. Logout

## Run

```bash
API_BASE=http://localhost:8080/api /Users/cukw/FinalWork/scripts/e2e_smoke.sh
```

If your gateway is on default `http://localhost:8080`, `API_BASE` can be omitted.
