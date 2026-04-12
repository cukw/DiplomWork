CREATE TABLE IF NOT EXISTS app_settings_documents (
    id           INTEGER PRIMARY KEY,
    payload_json TEXT NOT NULL,
    updated_at   TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS alert_rules (
    id                UUID PRIMARY KEY,
    name              VARCHAR(255) NOT NULL,
    enabled           BOOLEAN NOT NULL DEFAULT TRUE,
    severity          VARCHAR(32) NOT NULL,
    metric            VARCHAR(64) NOT NULL,
    operator          VARCHAR(16) NOT NULL,
    threshold         NUMERIC(18,4) NOT NULL,
    window_minutes    INTEGER NOT NULL,
    activity_type     VARCHAR(64),
    user_id           INTEGER,
    computer_id       INTEGER,
    notify_in_app     BOOLEAN NOT NULL DEFAULT TRUE,
    notify_email      BOOLEAN NOT NULL DEFAULT FALSE,
    cooldown_minutes  INTEGER NOT NULL DEFAULT 10,
    created_at        TIMESTAMP NOT NULL DEFAULT NOW(),
    updated_at        TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_alert_rules_enabled
    ON alert_rules(enabled);
CREATE INDEX IF NOT EXISTS idx_alert_rules_metric
    ON alert_rules(metric);

CREATE TABLE IF NOT EXISTS admin_audit_events (
    id          BIGSERIAL PRIMARY KEY,
    action      VARCHAR(128) NOT NULL,
    actor       VARCHAR(128) NOT NULL,
    target_type VARCHAR(64) NOT NULL,
    target_id   VARCHAR(128) NOT NULL,
    success     BOOLEAN NOT NULL DEFAULT TRUE,
    status_code INTEGER NULL,
    details_json TEXT NOT NULL DEFAULT '{}',
    created_at  TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_admin_audit_events_created_at
    ON admin_audit_events(created_at DESC);
CREATE INDEX IF NOT EXISTS idx_admin_audit_events_action
    ON admin_audit_events(action);
CREATE INDEX IF NOT EXISTS idx_admin_audit_events_actor
    ON admin_audit_events(actor);
CREATE INDEX IF NOT EXISTS idx_admin_audit_events_target
    ON admin_audit_events(target_type, target_id);
