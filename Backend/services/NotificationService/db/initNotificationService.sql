CREATE TABLE notifications (
    id          SERIAL PRIMARY KEY,
    user_id     INTEGER, -- Убрали внешнюю ссылку на users из другой БД
    type        VARCHAR(50),             -- 'anomaly', 'report_ready', ...
    title       VARCHAR(255),
    message     TEXT,
    is_read     BOOLEAN DEFAULT FALSE,
    sent_at     TIMESTAMP,
    recipient_email VARCHAR(320),
    channel     VARCHAR(20) DEFAULT 'email',   -- email / ui / telegram и т.п.
    delivery_status VARCHAR(32) NOT NULL DEFAULT 'pending',
    delivery_attempts INTEGER NOT NULL DEFAULT 0,
    max_delivery_attempts INTEGER NOT NULL DEFAULT 3,
    last_delivery_error TEXT,
    next_retry_at TIMESTAMPTZ,
    delivered_at TIMESTAMPTZ
);

CREATE TABLE notification_templates (
    id              SERIAL PRIMARY KEY,
    type            VARCHAR(50) UNIQUE NOT NULL,
    subject         VARCHAR(255),
    body_template   TEXT
);

CREATE INDEX idx_notifications_user_id ON notifications(user_id);
CREATE INDEX idx_notifications_is_read ON notifications(is_read);
CREATE INDEX idx_notifications_recipient_email ON notifications(recipient_email);
CREATE INDEX idx_notifications_delivery_status ON notifications(delivery_status);
CREATE INDEX idx_notifications_next_retry_at ON notifications(next_retry_at);

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

CREATE TABLE notification_delivery_dlq (
    id BIGSERIAL PRIMARY KEY,
    notification_id INTEGER NOT NULL,
    channel VARCHAR(20) NOT NULL DEFAULT 'in_app',
    recipient_email VARCHAR(320),
    attempts INTEGER NOT NULL DEFAULT 0,
    reason TEXT NOT NULL DEFAULT '',
    failed_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX uq_notification_delivery_dlq_notification_id
    ON notification_delivery_dlq(notification_id);
CREATE INDEX idx_notification_delivery_dlq_failed_at
    ON notification_delivery_dlq(failed_at);
