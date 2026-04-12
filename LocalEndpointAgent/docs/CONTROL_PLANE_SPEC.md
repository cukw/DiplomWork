# Control Plane Spec (for admin panel -> local agent)

Status: control-plane RPC/messages (`policy + commands`) добавлены в `AgentManagementService`, `gateway` и локальный агент.
Осталось: runtime e2e проверка с настоящим endpoint-хостом и артефактами self-update.

Чтобы локальный агент полностью управлялся из вашей админ-панели, `AgentManagementService` нужно расширить как минимум следующими сущностями и RPC.

## Что должно храниться на сервере
- `AgentPolicy` (по `computer_id` или `agent_id`)
  - интервалы сбора/heartbeat/flush
  - включение/выключение коллекторов (process/browser/active_window/idle)
  - порог idle
  - порог высокого риска
  - `auto_lock_enabled`
  - `admin_blocked` + `blocked_reason`
  - `policy_version`
  - `AgentCommand`
  - `BLOCK_WORKSTATION`
  - `UNBLOCK_WORKSTATION`
  - `FORCE_SYNC`
  - `SELF_UPDATE`
  - `RESTART_AGENT`
  - `UPDATE_POLICY`
  - статус выполнения (`pending/running/success/failed/ignored/timeout/deadletter`)

## Минимальные gRPC методы
- `GetAgentPolicy(GetAgentPolicyRequest) returns (GetAgentPolicyResponse)`
- `UpsertAgentPolicy(UpsertAgentPolicyRequest) returns (UpsertAgentPolicyResponse)`
- `GetPendingAgentCommands(GetPendingAgentCommandsRequest) returns (GetPendingAgentCommandsResponse)`
- `AckAgentCommand(AckAgentCommandRequest) returns (AckAgentCommandResponse)`
- `CreateAgentCommand(CreateAgentCommandRequest) returns (CreateAgentCommandResponse)`

## Что уже готово в агенте
- direct gRPC к `ActivityService` и `AgentManagementService`
- локальный `policy_cache.json`
- periodic `fetch_policy()` и `fetch_commands()`
- heartbeat health snapshot: очередь, последние сбор/отправка/ошибка, capabilities и статусы collector-ов
- обработчики команд `BLOCK_WORKSTATION`, `UNBLOCK_WORKSTATION`, `FORCE_SYNC`, `UPDATE_POLICY`/`REFRESH_POLICY`, `RESTART_AGENT`, `SELF_UPDATE`
- применение policy allow/deny списков в локальном risk engine

## Что нужно сделать в админ-панели
В `Settings`/`Agents` добавить UI:
- форма policy (интервалы, коллекторы, risk threshold, auto lock)
- действия `Block / Unblock workstation`
- статус последнего heartbeat / очереди / применённой policy version
