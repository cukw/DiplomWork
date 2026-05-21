# README: сборки проекта

Этот файл содержит практические команды сборки для всех частей проекта:
- общий стек через Docker Compose,
- backend/frontend локально,
- `LocalEndpointAgent` для macOS/Linux/Windows.

## 1. Требования

### Базовые
- Docker Desktop / Docker Engine + Docker Compose
- Git

### Для локальной (не Docker) сборки
- .NET SDK 10.0 (основные сервисы)
- .NET SDK 8.0 (`ActivityAgent`)
- Node.js 18+ (frontend)
- Python 3.11+ (LocalEndpointAgent)

### Для сборок агента
- macOS: `python3`, `hdiutil`
- Linux `.deb`: Docker daemon (сборка выполняется в контейнере)
- Windows `.exe`:
  - предпочтительно Windows host + PowerShell скрипт,
  - либо GitHub Actions (`windows-latest`)

## 2. Сборка и запуск всего стека (рекомендуется)

Из корня репозитория:

```bash
docker compose up --build -d
```

Проверка:

```bash
docker compose ps
docker compose logs -f gateway
```

Остановка:

```bash
docker compose down
```

## 3. Локальная сборка backend/frontend (без Docker)

### Backend (.NET)

```bash
dotnet restore /Users/cukw/FinalWork/FinalWork.sln
dotnet build /Users/cukw/FinalWork/FinalWork.sln
```

### Frontend (React)

```bash
cd /Users/cukw/FinalWork/Frontend
npm install
npm run build
```

Результат frontend-сборки:
- `/Users/cukw/FinalWork/Frontend/build`

## 4. LocalEndpointAgent: сборка пакетов

Рабочая директория:

```bash
cd /Users/cukw/FinalWork
```

### 4.1 macOS (бинарник + dmg)

```bash
bash /Users/cukw/FinalWork/LocalEndpointAgent/scripts/build_macos_dmg.sh
```

Артефакты:
- `/Users/cukw/FinalWork/LocalEndpointAgent/dist/macos/endpoint-agent-macos`
- `/Users/cukw/FinalWork/LocalEndpointAgent/dist/macos/endpoint-agent-macos.dmg`

### 4.2 Linux (бинарник + .deb)

```bash
bash /Users/cukw/FinalWork/LocalEndpointAgent/scripts/build_linux_deb.sh
```

Артефакты:
- `/Users/cukw/FinalWork/LocalEndpointAgent/dist/linux/endpoint-agent-linux`
- `/Users/cukw/FinalWork/LocalEndpointAgent/dist/linux/local-endpoint-agent_0.1.0_amd64.deb`

### 4.3 Windows (.exe)

#### Вариант A (рекомендуется): на Windows host

```powershell
powershell -ExecutionPolicy Bypass -File C:\path\to\FinalWork\LocalEndpointAgent\scripts\build_windows_exe.ps1
```

Артефакт:
- `C:\path\to\FinalWork\LocalEndpointAgent\dist\windows\endpoint-agent-windows.exe`

#### Вариант B: через CI (GitHub Actions)
- workflow: `Local Endpoint Agent Packages`
- артефакты:
  - `local-endpoint-agent-windows`
  - `local-endpoint-agent-linux`
  - `local-endpoint-agent-macos`
- запускается автоматически на `push`/tag и вручную через `workflow_dispatch`
- shell-запуск вручную: `gh workflow run "Local Endpoint Agent Packages"`

Перед prod/release сборкой:
- добавить GitHub repository secret `AGENT_AUTH_TOKEN`
- jobs для Windows/Linux/macOS зашивают в agent artifact:
  - `gateway_url=https://2.26.89.86`
  - `activity_service_url=2.26.89.86:5001`
  - `agent_management_url=2.26.89.86:5015`
  - `agent_auth_header=x-agent-token`
  - `agent_auth_token=$AGENT_AUTH_TOKEN`

Операционная схема:
- администратор скачивает GitHub artifact/release asset и ставит агент на ПК;
- установщик/EXE не логинит пользователя;
- пользователь авторизуется уже в локальном окне агента при первом запуске.

Важно:
- `build_windows_exe.sh` (Docker + wine) не поддерживается на ARM-host (macOS ARM/Linux ARM).

### 4.4 Сборка «всего» для агента

```bash
bash /Users/cukw/FinalWork/LocalEndpointAgent/scripts/build_all_packages.sh
```

Поведение:
- собирает macOS, Linux,
- для Windows пытается Docker build только на поддерживаемой архитектуре,
- на macOS ARM выводит корректную подсказку про `build_windows_exe.ps1`/CI.

## 5. Быстрые проверки артефактов

```bash
find /Users/cukw/FinalWork/LocalEndpointAgent/dist -maxdepth 3 -type f | sort
```

## 6. Частые проблемы и решения

### Docker daemon не запущен
Ошибка вида:
- `Docker daemon is not running`

Решение:
- запустить Docker Desktop/Engine,
- повторить команду сборки.

### Не собирается Windows `.exe` на Mac ARM
Причина:
- ограничение `wine` в режиме эмуляции amd64.

Решение:
- собирать `.exe` на Windows (`build_windows_exe.ps1`) или в GitHub Actions.

### gRPC stubs не найдены у агента
Решение:

```bash
bash /Users/cukw/FinalWork/LocalEndpointAgent/scripts/generate_protos.sh
```

## 7. Минимальный чек-лист перед релизом

1. `docker compose up --build -d` проходит без падений.
2. `dotnet build FinalWork.sln` проходит локально.
3. `npm run build` в `Frontend` проходит.
4. В `LocalEndpointAgent/dist` есть нужные артефакты для целевых ОС.
5. Для Windows/Linux/macOS есть agent artifacts из GitHub Actions или release assets.
