INSERT INTO role_permissions (role_name, permission)
VALUES
    ('user', 'user.enrollcomputer'),
    ('user', 'user.endcomputersession'),
    ('moderator', 'user.enrollcomputer'),
    ('moderator', 'user.endcomputersession')
ON CONFLICT (role_name, permission) DO NOTHING;
