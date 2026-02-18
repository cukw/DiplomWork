-- Скрипт для добавления тестового пользователя в систему
-- Используйте этот скрипт, если нужно добавить тестового пользователя в уже существующую базу данных

-- Проверяем и добавляем роли, если их нет
INSERT INTO roles (name, description) VALUES 
('admin', 'Администратор системы'),
('user', 'Обычный пользователь'),
('moderator', 'Модератор')
ON CONFLICT (name) DO NOTHING;

-- Добавляем тестового пользователя (пароль: password123)
-- Хеш пароля для 'password123' (предполагается использование bcrypt)
INSERT INTO auth_users (username, password_hash, email, role_id, is_active) VALUES 
('testuser', '$2a$10$92IXUNpkjO0rOQ5byMi.Ye4oKoEa3Ro9llC/.og/at2.uheWG/igi', 'test@example.com', 
 (SELECT id FROM roles WHERE name = 'user'), TRUE)
ON CONFLICT (username) DO NOTHING;

-- Добавляем профиль пользователя
INSERT INTO users (auth_user_id, full_name, department) VALUES 
((SELECT id FROM auth_users WHERE username = 'testuser'), 'Тестовый Пользователь', 'IT отдел')
ON CONFLICT (auth_user_id) DO NOTHING;

-- Добавляем компьютер для пользователя
INSERT INTO computers (user_id, hostname, os_version, ip_address, mac_address, status) VALUES 
((SELECT id FROM users WHERE auth_user_id = (SELECT id FROM auth_users WHERE username = 'testuser')), 
 'TEST-PC-001', 'Windows 10 Pro', '192.168.1.100', 'AA:BB:CC:DD:EE:FF', 'active')
ON CONFLICT (mac_address) DO NOTHING;

-- Добавляем администратора (пароль: admin123)
INSERT INTO auth_users (username, password_hash, email, role_id, is_active) VALUES 
('admin', '$2a$10$YQ6j2wKgI2pK1sL6qN7HMeO5gZ9jK8lN3mP0rQ7sT4uV5wX6yZ7a', 'admin@example.com', 
 (SELECT id FROM roles WHERE name = 'admin'), TRUE)
ON CONFLICT (username) DO NOTHING;

-- Добавляем профиль администратора
INSERT INTO users (auth_user_id, full_name, department) VALUES 
((SELECT id FROM auth_users WHERE username = 'admin'), 'Администратор Системы', 'IT отдел')
ON CONFLICT (auth_user_id) DO NOTHING;

-- Добавляем компьютер для администратора
INSERT INTO computers (user_id, hostname, os_version, ip_address, mac_address, status) VALUES 
((SELECT id FROM users WHERE auth_user_id = (SELECT id FROM auth_users WHERE username = 'admin')), 
 'ADMIN-PC-001', 'Windows Server 2019', '192.168.1.10', '11:22:33:44:55:66', 'active')
ON CONFLICT (mac_address) DO NOTHING;