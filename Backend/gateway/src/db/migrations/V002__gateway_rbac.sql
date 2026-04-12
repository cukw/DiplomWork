CREATE TABLE IF NOT EXISTS role_permissions (
    id BIGSERIAL PRIMARY KEY,
    role_name VARCHAR(128) NOT NULL,
    permission VARCHAR(256) NOT NULL,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP NOT NULL DEFAULT NOW(),
    UNIQUE(role_name, permission)
);

CREATE INDEX IF NOT EXISTS idx_role_permissions_role_name
    ON role_permissions(role_name);
CREATE INDEX IF NOT EXISTS idx_role_permissions_permission
    ON role_permissions(permission);
