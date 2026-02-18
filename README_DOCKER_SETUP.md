# Настройка и запуск системы с Docker

## Обзор

Система мониторинга активности пользователей состоит из множества микросервисов, которые работают вместе через Docker Compose. Фронтенд и бэкенд полностью настроены для взаимодействия через API Gateway.

## Структура системы

### Бэкенд сервисы
- **API Gateway** (порт 8080) - Центральная точка входа для всех API запросов
- **AuthService** (порт 5003) - Аутентификация и авторизация пользователей
- **UserService** (порт 5004) - Управление пользователями и компьютерами
- **ActivityService** (порт 5001) - Сбор и анализ активности
- **MetricsService** (порт 5005) - Сбор метрик
- **NotificationService** (порт 5006) - Уведомления
- **ReportService** (порт 5007) - Генерация отчетов
- **AgentManagementService** (порт 5008) - Управление агентами

### Базы данных
- **postgres-auth** (порт 5433) - База данных аутентификации
- **postgres-user** (порт 5434) - База данных пользователей
- **postgres-activity** (порт 5432) - База данных активности
- **postgres-metrics** (порт 5435) - База данных метрик
- **postgres-notification** (порт 5436) - База данных уведомлений
- **postgres-report** (порт 5437) - База данных отчетов
- **postgres-agent** (порт 5438) - База данных агентов

### Фронтенд
- **Frontend** (порт 3000) - React приложение с nginx

### Очередь сообщений
- **RabbitMQ** (порты 5672, 15672) - Очередь для асинхронной коммуникации

## Быстрый запуск

### 1. Запуск всех сервисов
```bash
docker-compose up -d
```

### 2. Добавление тестовых пользователей
```bash
./Backend/services/UserService/db/add_test_users.sh
```

### 3. Доступ к системе
- **Фронтенд**: http://localhost:3000
- **API Gateway**: http://localhost:8080
- **RabbitMQ Management**: http://localhost:15672 (guest/guest)

## Тестовые пользователи

### Обычный пользователь
- **Логин**: `testuser`
- **Пароль**: `password123`

### Администратор
- **Логин**: `admin`
- **Пароль**: `admin123`

## API Эндпоинты

### Аутентификация
- `POST /api/auth/login` - Вход в систему
- `POST /api/auth/logout` - Выход из системы
- `GET /api/auth/me` - Текущий пользователь

### Дашборд
- `GET /api/dashboard/stats` - Статистика
- `GET /api/dashboard/activities` - Последние активности
- `GET /api/dashboard/anomalies` - Последние аномалии

### Поиск
- `GET /api/search/activities` - Поиск активностей
- `GET /api/search/anomalies` - Поиск аномалий
- `GET /api/search/filters` - Фильтры поиска

### Отчеты
- `GET /api/reports/daily` - Дневные отчеты
- `GET /api/reports/weekly` - Недельные отчеты
- `GET /api/reports/monthly` - Месячные отчеты
- `GET /api/reports/custom` - Пользовательские отчеты

## Разработка

### Запуск только фронтенда для разработки
```bash
cd Frontend
npm start
```

### Пересборка конкретного сервиса
```bash
docker-compose up -d --build frontend
docker-compose up -d --build authservice
docker-compose up -d --build activityservice
```

### Просмотр логов
```bash
# Все сервисы
docker-compose logs -f

# Конкретный сервис
docker-compose logs -f frontend
docker-compose logs -f authservice
```

### Остановка системы
```bash
# Остановка всех контейнеров
docker-compose down

# Остановка с удалением образов
docker-compose down --rmi all

# Полная очистка (включая тома)
docker-compose down -v --rmi all
```

## Управление базами данных

### Подключение к базам данных
```bash
# База аутентификации
docker-compose exec postgres-auth psql -U postgres -d auth

# База пользователей
docker-compose exec postgres-user psql -U postgres -d users

# База активности
docker-compose exec postgres-activity psql -U postgres -d activities
```

### Добавление тестовых данных
```bash
# Выполнить скрипт добавления тестовых пользователей
./Backend/services/UserService/db/add_test_users.sh
```

## Мониторинг и отладка

### Проверка здоровья сервисов
```bash
# Проверить статус всех сервисов
docker-compose ps

# Проверить здоровье конкретного сервиса
curl http://localhost:8080/health/auth
curl http://localhost:8080/health/user
curl http://localhost:8080/health/activity
```

### Просмотр сетевых соединений
```bash
# Показать все сети
docker network ls

# Показать контейнеры в сети
docker network inspect finalwork_backend
docker network inspect finalwork_frontend
```

## Проблемы и решения

### Фронтенд не может подключиться к API
1. Убедитесь, что API Gateway запущен: `docker-compose ps gateway`
2. Проверьте логи gateway: `docker-compose logs gateway`
3. Убедитесь, что фронтенд в той же сети: `docker network inspect finalwork_backend`

### Ошибка аутентификации
1. Проверьте, что тестовые пользователи добавлены
2. Проверьте логи AuthService: `docker-compose logs authservice`
3. Убедитесь, что база данных auth запущена: `docker-compose ps postgres-auth`

### Медленная работа системы
1. Проверьте использование ресурсов: `docker stats`
2. Очистите неиспользуемые образы: `docker system prune -a`
3. Проверьте логи на предмет ошибок

## Обновление системы

### Обновление конкретного сервиса
```bash
docker-compose pull activityservice
docker-compose up -d activityservice
```

### Полное обновление
```bash
docker-compose pull
docker-compose up -d
```

## Резервное копирование

### Резервное копирование баз данных
```bash
# Создать директорию для бэкапов
mkdir -p backups

# Бэкап базы данных пользователей
docker-compose exec postgres-user pg_dump -U postgres users > backups/users_backup.sql

# Бэкап базы данных активности
docker-compose exec postgres-activity pg_dump -U postgres activities > backups/activities_backup.sql
```

### Восстановление из бэкапа
```bash
# Восстановление базы данных пользователей
docker-compose exec -T postgres-user psql -U postgres users < backups/users_backup.sql

# Восстановление базы данных активности
docker-compose exec -T postgres-activity psql -U postgres activities < backups/activities_backup.sql