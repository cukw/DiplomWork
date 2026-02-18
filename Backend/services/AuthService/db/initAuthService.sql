-- Роли: user / admin / moderator
CREATE TABLE roles (
    id          SERIAL PRIMARY KEY,
    name        VARCHAR(50) UNIQUE NOT NULL, -- 'user', 'admin', 'moderator'
    description TEXT,
    created_at  TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Пользователи (учетные записи для логина)
CREATE TABLE auth_users (
    id              SERIAL PRIMARY KEY,
    username        VARCHAR(100) UNIQUE NOT NULL,
    password_hash   VARCHAR(255) NOT NULL,
    email           VARCHAR(255) UNIQUE,
    role_id         INTEGER REFERENCES roles(id),
    last_login      TIMESTAMP,
    is_active       BOOLEAN DEFAULT TRUE,
    created_at      TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Сессии / токены
CREATE TABLE sessions (
    id          SERIAL PRIMARY KEY,
    user_id     INTEGER NOT NULL REFERENCES auth_users(id),
    token_hash  VARCHAR(255) UNIQUE NOT NULL,
    expires_at  TIMESTAMP NOT NULL,
    created_at  TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Индексы
CREATE INDEX idx_auth_users_role_id ON auth_users(role_id);
CREATE INDEX idx_sessions_user_id ON sessions(user_id);
CREATE INDEX idx_sessions_expires_at ON sessions(expires_at);

-- Добавление тестовых ролей
INSERT INTO roles (name, description) VALUES
('admin', 'Администратор системы'),
('user', 'Обычный пользователь'),
('moderator', 'Модератор')
ON CONFLICT (name) DO NOTHING;

-- Добавление тестового пользователя (пароль: password123)
-- Хеш пароля для 'password123' (предполагается использование bcrypt)
INSERT INTO auth_users (username, password_hash, email, role_id, is_active) VALUES
('testuser', '$2a$10$92IXUNpkjO0rOQ5byMi.Ye4oKoEa3Ro9llC/.og/at2.uheWG/igi', 'test@example.com', 2, TRUE)
ON CONFLICT (username) DO NOTHING;