-- Общая сущность метрики / набора правил
CREATE TABLE metrics (
    id          SERIAL PRIMARY KEY,
    user_id     INTEGER REFERENCES users(id),
    -- тип метрики: 'process', 'site', 'file', 'generic', etc
    type        VARCHAR(50) NOT NULL,
    -- произвольная конфигурация (JSON): пороги, режимы, дополнительные настройки
    config      JSONB NOT NULL,
    is_active   BOOLEAN DEFAULT TRUE,
    updated_at  TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Вайтлист (разрешённые)
CREATE TABLE whitelists (
    id          SERIAL PRIMARY KEY,
    metric_id   INTEGER NOT NULL REFERENCES metrics(id) ON DELETE CASCADE,
    -- pattern может быть домен, путь к файлу, имя процесса и т.п.
    pattern     VARCHAR(500) NOT NULL,
    -- действие на совпадение, по умолчанию allow
    action      VARCHAR(20) DEFAULT 'allow',
    created_at  TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Блеклист (запрещённые)
CREATE TABLE blacklists (
    id          SERIAL PRIMARY KEY,
    metric_id   INTEGER NOT NULL REFERENCES metrics(id) ON DELETE CASCADE,
    pattern     VARCHAR(500) NOT NULL,
    action      VARCHAR(20) DEFAULT 'block',
    created_at  TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_metrics_user_id ON metrics(user_id);
CREATE INDEX idx_whitelists_metric_id ON whitelists(metric_id);
CREATE INDEX idx_blacklists_metric_id ON blacklists(metric_id);
