-- Бизнес-профиль пользователя (без внешней ссылки на auth_users)
CREATE TABLE users (
    id              SERIAL PRIMARY KEY,
    auth_user_id    INTEGER UNIQUE, -- Убрали внешнюю ссылку, так как auth_users в другой БД
    full_name       VARCHAR(255),
    department      VARCHAR(100),
    created_at      TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Компьютеры (один компьютер принадлежит одному пользователю; пользователь может иметь несколько ПК)
CREATE TABLE computers (
    id              SERIAL PRIMARY KEY,
    user_id         INTEGER NULL REFERENCES users(id) ON DELETE SET NULL,
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

CREATE TABLE computer_sessions (
    id              BIGSERIAL PRIMARY KEY,
    user_id         INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    auth_user_id    INTEGER NOT NULL,
    computer_id     INTEGER NOT NULL REFERENCES computers(id) ON DELETE CASCADE,
    started_at      TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    expires_at      TIMESTAMP NOT NULL,
    ended_at        TIMESTAMP NULL,
    last_seen       TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    status          VARCHAR(20) NOT NULL DEFAULT 'active'
);

CREATE INDEX idx_computer_sessions_user_id ON computer_sessions(user_id);
CREATE INDEX idx_computer_sessions_computer_id ON computer_sessions(computer_id);
CREATE UNIQUE INDEX uq_computer_sessions_active_user ON computer_sessions(user_id) WHERE ended_at IS NULL;
CREATE UNIQUE INDEX uq_computer_sessions_active_computer ON computer_sessions(computer_id) WHERE ended_at IS NULL;
