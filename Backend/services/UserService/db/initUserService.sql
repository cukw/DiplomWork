-- Бизнес-профиль пользователя (1 к 1 с auth_users, если нужно)
CREATE TABLE users (
    id              SERIAL PRIMARY KEY,
    auth_user_id    INTEGER UNIQUE REFERENCES auth_users(id),
    full_name       VARCHAR(255),
    department      VARCHAR(100),
    created_at      TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Компьютеры (один компьютер строго за одним пользователем)
CREATE TABLE computers (
    id              SERIAL PRIMARY KEY,
    user_id         INTEGER UNIQUE REFERENCES users(id), -- one-to-one
    hostname        VARCHAR(255) NOT NULL,
    os_version      VARCHAR(100),
    ip_address      INET,
    mac_address     VARCHAR(17) UNIQUE,
    status          VARCHAR(20) DEFAULT 'active', -- active / disabled / retired
    last_seen       TIMESTAMP,
    created_at      TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_computers_user_id ON computers(user_id);
CREATE INDEX idx_computers_hostname ON computers(hostname);

-- Добавление тестового пользователя (связанного с testuser из auth_users)
INSERT INTO users (auth_user_id, full_name, department) VALUES
(1, 'Тестовый Пользователь', 'IT отдел')
ON CONFLICT (auth_user_id) DO NOTHING;

-- Добавление тестового компьютера для пользователя
INSERT INTO computers (user_id, hostname, os_version, ip_address, mac_address, status) VALUES
(1, 'TEST-PC-001', 'Windows 10 Pro', '192.168.1.100', 'AA:BB:CC:DD:EE:FF', 'active')
ON CONFLICT (mac_address) DO NOTHING;