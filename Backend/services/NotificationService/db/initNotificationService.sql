CREATE TABLE notifications (
    id          SERIAL PRIMARY KEY,
    user_id     INTEGER REFERENCES users(id),
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