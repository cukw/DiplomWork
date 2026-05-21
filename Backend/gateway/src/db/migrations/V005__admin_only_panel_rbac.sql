DELETE FROM role_permissions
WHERE role_name <> 'admin'
  AND NOT (
      role_name = 'user'
      AND permission IN ('user.enrollcomputer', 'user.endcomputersession')
  );

DELETE FROM role_permissions
WHERE role_name = 'user'
  AND permission NOT IN ('user.enrollcomputer', 'user.endcomputersession');

INSERT INTO role_permissions (role_name, permission)
VALUES
    ('admin', '*'),
    ('user', 'user.enrollcomputer'),
    ('user', 'user.endcomputersession')
ON CONFLICT (role_name, permission) DO NOTHING;
