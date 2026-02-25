CREATE TABLE notifications (
    id          SERIAL PRIMARY KEY,
    user_id     INTEGER, -- Убрали внешнюю ссылку на users из другой БД
    type        VARCHAR(50),             -- 'anomaly', 'report_ready', ...
    title       VARCHAR(255),
    message     TEXT,
    is_read     BOOLEAN DEFAULT FALSE,
    sent_at     TIMESTAMP,
    channel     VARCHAR(20) DEFAULT 'email'   -- email / ui / telegram и т.п.
);

CREATE TABLE notification_templates (
    id              SERIAL PRIMARY KEY,
    type            VARCHAR(50) UNIQUE NOT NULL,
    subject         VARCHAR(255),
    body_template   TEXT
);

CREATE INDEX idx_notifications_user_id ON notifications(user_id);
CREATE INDEX idx_notifications_is_read ON notifications(is_read);

CREATE TABLE processed_event_inbox (
    id          BIGSERIAL PRIMARY KEY,
    consumer    VARCHAR(128) NOT NULL,
    event_key   VARCHAR(256) NOT NULL,
    message_id  VARCHAR(128),
    processed_at TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX uq_processed_event_inbox_consumer_event_key
    ON processed_event_inbox(consumer, event_key);
CREATE INDEX idx_processed_event_inbox_processed_at
    ON processed_event_inbox(processed_at);
