#!/bin/bash

# Скрипт для добавления тестовых пользователей в систему
# Используйте этот скрипт для быстрого добавления тестовых пользователей в уже работающую систему

echo "Добавление тестовых пользователей в систему..."

# Проверяем, запущен ли docker-compose
if ! docker-compose ps | grep -q "Up"; then
    echo "Ошибка: Docker-compose не запущен. Пожалуйста, запустите систему с помощью 'docker-compose up -d'"
    exit 1
fi

# Добавляем тестовых пользователей в базу данных авторизации
echo "Добавление пользователей в базу данных авторизации..."
docker-compose exec -T postgres-auth psql -U postgres -d auth -c "
INSERT INTO auth_users (username, password_hash, email, role_id, is_active) VALUES
('testuser', '\$2a\$10\$92IXUNpkjO0rOQ5byMi.Ye4oKoEa3Ro9llC/.og/at2.uheWG/igi', 'test@example.com',
 (SELECT id FROM roles WHERE name = 'user'), TRUE),
('admin', '\$2a\$10\$YQ6j2wKgI2pK1sL6qN7HMeO5gZ9jK8lN3mP0rQ7sT4uV5wX6yZ7a', 'admin@example.com',
 (SELECT id FROM roles WHERE name = 'admin'), TRUE)
ON CONFLICT (username) DO NOTHING;
"

if [ $? -eq 0 ]; then
    echo "✅ Тестовые пользователи успешно добавлены!"
    echo ""
    echo "Данные для входа:"
    echo "1. Обычный пользователь:"
    echo "   - Логин: testuser"
    echo "   - Пароль: password123"
    echo ""
    echo "2. Администратор:"
    echo "   - Логин: admin"
    echo "   - Пароль: admin123"
else
    echo "❌ Ошибка при добавлении тестовых пользователей"
    exit 1
fi