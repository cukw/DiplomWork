# Activity Monitoring System

Система мониторинга активности пользователей для обнаружения аномалий и обеспечения безопасности.

## Архитектура

Система построена на микросервисной архитектуре со следующими компонентами:

### Backend Services

- **ActivityService** - Сервис сбора и анализа активности пользователей
- **AuthService** - Сервис аутентификации и авторизации
- **UserService** - Сервис управления пользователями и компьютерами
- **NotificationService** - Сервис уведомлений
- **ReportService** - Сервис генерации отчетов
- **MetricsService** - Сервис сбора метрик
- **AgentManagementService** - Сервис управления агентами
- **ActivityAgent** - Агент сбора активности на компьютерах
- **Gateway** - API Gateway для маршрутизации запросов

### Frontend

- React-приложение с Material-UI для визуализации данных

## Развертывание

### Требования

- Docker
- Docker Compose
- .NET 10.0 SDK
- Node.js 18+

### Запуск системы

1. Клонируйте репозиторий:
```bash
git clone <repository-url>
cd FinalWork
```

2. Запустите все сервисы с помощью Docker Compose:
```bash
docker-compose up -d
```

3. Доступ к сервисам:
- Frontend: http://localhost:3000
- API Gateway: http://localhost:8080
- RabbitMQ Management: http://localhost:15672 (guest/guest)

### Структура портов

| Сервис | Порт |
|---------|------|
| Frontend | 3000 |
| API Gateway | 8080 |
| ActivityService | 5001, 5002 |
| AuthService | 5003 |
| UserService | 5004 |
| MetricsService | 5005 |
| NotificationService | 5006 |
| ReportService | 5007 |
| AgentManagementService | 5008 |
| PostgreSQL (Activity) | 5432 |
| PostgreSQL (Auth) | 5433 |
| PostgreSQL (User) | 5434 |
| PostgreSQL (Metrics) | 5435 |
| PostgreSQL (Notification) | 5436 |
| PostgreSQL (Report) | 5437 |
| PostgreSQL (Agent) | 5438 |
| RabbitMQ | 5672, 15672 |

## Функциональность

### Мониторинг активности

- Сбор активности процессов, сетевых подключений и доступа к файлам
- Определение риска активности на основе различных параметров
- Обнаружение аномалий с использованием расширенных правил

### Правила обнаружения аномалий

1. **Высокий уровень риска** - активности с оценкой риска ≥ 80
2. **Подозрительные типы активности** - MALWARE, DATA_EXFILTRATION, UNAUTHORIZED_ACCESS
3. **Необычная продолжительность** - активности дольше 24 часов
4. **Заблокированные активности** - активности, заблокированные системой безопасности
5. **Повторяющиеся активности** - более 10 однотипных активностей за час
6. **Подозрительные URL** - доступ к известным вредоносным доменам
7. **Активность в необычное время** - активности вне рабочего времени (9:00-18:00)
8. **Процессы высокого риска** - запуск известных вредоносных программ
9. **Доступ к чувствительным файлам** - доступ к файлам с паролями, ключами и т.д.
10. **Чрезмерная сетевая активность** - более 20 сетевых подключений за 5 минут

### Уведомления

- Автоматическая отправка уведомлений при обнаружении аномалий
- Приоритизация уведомлений в зависимости от типа аномалии
- Поддержка различных каналов уведомлений (email, in-app)

### Отчеты

- Ежедневные, еженедельные и ежемесячные отчеты
- Пользовательские отчеты за произвольный период
- Визуализация данных с графиками и диаграммами

### Поиск и фильтрация

- Поиск по всем полям активности
- Фильтрация по типу активности, компьютеру, временному периоду
- Пагинация результатов

## Разработка

### Запуск бэкенда

```bash
cd Backend/services/<ServiceName>
dotnet run
```

### Запуск фронтенда

```bash
cd Frontend
npm install
npm start
```

### Миграции баз данных

Базы данных инициализируются автоматически при первом запуске через SQL-скрипты в папках `db/` каждого сервиса.

## Конфигурация

### Переменные окружения

Основные переменные окружения можно настроить в `docker-compose.yml`:

- `ConnectionStrings__DefaultConnection` - строки подключения к базам данных
- `RabbitMQ__Host`, `RabbitMQ__User`, `RabbitMQ__Password` - настройки RabbitMQ
- `Agent__ComputerId`, `Agent__CollectionInterval` - настройки агента

### Настройка агентов

Агенты можно настроить через переменные окружения:

```yaml
environment:
  ActivityService__Url: "http://activityservice:5001"
  Agent__ComputerId: "1"
  Agent__CollectionInterval: "5000"
  Agent__Enabled: "true"
```

## Мониторинг

### Health checks

Все сервисы предоставляют эндпоинт `/health` для проверки состояния:

- http://localhost:8080/health/activity
- http://localhost:8080/health/auth
- http://localhost:8080/health/user
- и т.д.

### Логи

Просмотр логов всех сервисов:
```bash
docker-compose logs -f
```

Просмотр логов конкретного сервиса:
```bash
docker-compose logs -f activityservice
```

## Безопасность

- Аутентификация через JWT токены
- Шифрование паролей
- Ограничение доступа к API через Gateway
- Rate limiting для предотвращения атак

## Тестирование

Для запуска тестов:

```bash
# Backend тесты
cd Backend/services/<ServiceName>.Tests
dotnet test

# Frontend тесты
cd Frontend
npm test
```

## Вклад

1. Fork репозитория
2. Создайте ветку функции (`git checkout -b feature/AmazingFeature`)
3. Закоммитьте изменения (`git commit -m 'Add some AmazingFeature'`)
4. Отправьте в ветку (`git push origin feature/AmazingFeature`)
5. Откройте Pull Request