# Тестовые пользователи

## Добавленные тестовые пользователи

### Обычный пользователь
- **Логин**: `testuser`
- **Пароль**: `password123`
- **Email**: `test@example.com`
- **Роль**: `user`
- **Отдел**: `IT отдел`
- **Компьютер**: TEST-PC-001 (Windows 10 Pro, IP: 192.168.1.100)

### Администратор
- **Логин**: `admin`
- **Пароль**: `admin123`
- **Email**: `admin@example.com`
- **Роль**: `admin`
- **Отдел**: `IT отдел`
- **Компьютер**: ADMIN-PC-001 (Windows Server 2019, IP: 192.168.1.10)

## Как добавить тестовых пользователей

### Вариант 1: При инициализации базы данных
Тестовые пользователи будут автоматически добавлены при инициализации базы данных, если вы используете файлы:
- `Backend/services/AuthService/db/initAuthService.sql`
- `Backend/services/UserService/db/initUserService.sql`

### Вариант 2: Добавление в существующую базу данных

#### Способ А: Использование готового скрипта (рекомендуется)
Выполните скрипт для автоматического добавления тестовых пользователей:

```bash
./Backend/services/UserService/db/add_test_users.sh
```

#### Способ Б: Выполнение SQL-скрипта вручную
Выполните SQL-скрипт `add_test_user.sql` в базе данных:

```bash
# Для PostgreSQL (прямое подключение)
psql -h localhost -U postgres -d auth -p 5433 -f Backend/services/UserService/db/add_test_user.sql

# Через Docker-compose для базы данных авторизации
docker-compose exec postgres-auth psql -U postgres -d auth -f /app/Backend/services/UserService/db/add_test_user.sql

# Через Docker-compose для базы данных пользователей
docker-compose exec postgres-user psql -U postgres -d users -f /app/Backend/services/UserService/db/add_test_user.sql
```

### Информация о подключении к базам данных

- **База данных авторизации**: `localhost:5433`, база: `auth`, пользователь: `postgres`, пароль: `pass`
- **База данных пользователей**: `localhost:5434`, база: `users`, пользователь: `postgres`, пароль: `pass`
- **База данных активностей**: `localhost:5432`, база: `activities`, пользователь: `postgres`, пароль: `pass`

## Примечания

- Пароли хешируются с использованием bcrypt
- Если пользователи с такими логинами уже существуют, они не будут перезаписаны (используется `ON CONFLICT DO NOTHING`)
- Каждый пользователь имеет связанный с ним компьютер в системе
- Администратор имеет права на управление системой