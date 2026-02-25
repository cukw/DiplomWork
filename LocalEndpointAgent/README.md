# LocalEndpointAgent (Python + Rust)

Локальный агент мониторинга активности пользователей для Activity Monitoring System.

## Что делает (MVP)
- собирает активность по процессам (`PROCESS_SNAPSHOT`)
- читает историю посещённых сайтов из локальных браузеров (`BROWSER_VISIT`)
- отслеживает idle time (`USER_IDLE`, `USER_ACTIVE`)
- отслеживает активное окно (`ACTIVE_WINDOW_CHANGE`)
- работает напрямую по gRPC с `ActivityService` и `AgentManagementService` (без gateway)
- хранит очередь событий локально (SQLite) и продолжает работу при потере связи
- применяет последние полученные политики (локальный cache)
- умеет блокировать рабочую станцию (soft-lock/lock workstation) при высоком риске

## Важно
- Агент **не реализует keylogging** и не собирает нажатия клавиш (это отдельный юридический/этический риск).
- «Блокировка компьютера» реализована как управляемая блокировка рабочей станции (`LockWorkStation`) + повторное применение при включённом флаге policy.
- Для полноценного управления из админ-панели нужно расширение `AgentManagementService` (policy + commands RPC). Агент уже имеет клиентские точки расширения и локальный cache.

## Структура
- `src/endpoint_agent/` — Python-агент
- `rust/sysprobe/` — Rust/PyO3 модуль для low-level системных вызовов
- `scripts/` — генерация gRPC stubs и автозагрузка
- `config/` — примеры конфигурации и policy

## Быстрый старт (dev)
1. Создать venv и установить зависимости:
   - `pip install -r requirements.txt`
2. Сгенерировать Python gRPC stubs:
   - `bash scripts/generate_protos.sh`
3. (Опционально) собрать Rust модуль:
   - `pip install maturin`
   - `maturin develop --manifest-path rust/sysprobe/Cargo.toml`
4. Запустить агент:
   - `python -m endpoint_agent.main --config config/agent.local.yaml`

## Кроссплатформенный установщик (Linux / macOS / Windows)
Добавлен единый установщик:
- `scripts/install_agent.py`

Что делает установщик:
- копирует `LocalEndpointAgent` в системный каталог пользователя
- создаёт `venv`
- устанавливает Python-зависимости
- генерирует Python gRPC stubs из `Backend/services/*/Protos`
- устанавливает пакет агента
- создаёт конфиг `agent.local.yaml`
- настраивает автозапуск:
  - Linux: `systemd --user` (или `~/.config/autostart` fallback)
  - macOS: `launchd` (`~/Library/LaunchAgents`)
  - Windows: `Scheduled Task` (или Startup folder fallback)

### Пример запуска установщика
Из корня репозитория:

```bash
python3 LocalEndpointAgent/scripts/install_agent.py \
  --computer-id 1 \
  --user-id 1 \
  --activity-service-url http://localhost:5001 \
  --agent-management-url http://localhost:5015
```

Полезные опции:
- `--install-dir <path>` — кастомная директория установки
- `--skip-autostart` — не настраивать автозапуск
- `--skip-rust` — не собирать Rust `sysprobe` (использовать Python fallback)
- `--force` — перезаписать существующую установку
- `--dry-run` — показать шаги без изменений

## Платформенные возможности (текущее состояние)
- `Windows`: полный путь для low-level функций через Rust `sysprobe` (или Python fallback, если Rust-модуль не собран)
- `macOS`: low-level функции работают через системные fallback-механизмы:
  - `idle time`: `ioreg`
  - `active window`: `osascript` / `System Events` (может потребовать Accessibility/Automation permissions)
  - `lock workstation`: `CGSession -suspend` (fallback: `pmset displaysleepnow`)
- `Linux`: low-level функции работают через доступные утилиты (если установлены):
  - `idle time`: `xprintidle` / `xssstate`
  - `active window`: `xdotool` / `xprop` (обычно X11; в Wayland может быть ограничено)
  - `lock workstation`: `loginctl`, `gnome-screensaver-command`, `dm-tool`, `qdbus*`

Если конкретная capability недоступна, агент продолжит работу (процессы, браузеры, очередь, gRPC, policy/commands), а неподдерживаемый коллектор будет автоматически отключён без падения процесса.

## Нужен ли AgentManagementService?
Да, нужен.

Его роль при наличии локального агента:
- регистрация агента и heartbeat
- хранение/выдача политики сбора активности
- выдача команд (например, `BLOCK_WORKSTATION` / `UNBLOCK_WORKSTATION`)
- история статусов/heartbeats/sync-batches
- связь с админ-панелью (control plane)

Без `AgentManagementService` локальный агент может только отправлять события в `ActivityService`, но централизованного управления (политики, блокировки, конфиг) не будет.
