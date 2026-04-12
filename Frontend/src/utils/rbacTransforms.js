export const normalizePermissionList = (rawPermissions) => {
  if (Array.isArray(rawPermissions)) {
    return rawPermissions
      .map((permission) => String(permission || '').trim().toLowerCase())
      .filter(Boolean);
  }

  return String(rawPermissions || '')
    .split(/[,;\n]/)
    .map((permission) => permission.trim().toLowerCase())
    .filter(Boolean);
};

export const mapRolePermissionsToRows = (rolePermissions) => Object.entries(rolePermissions || {})
  .map(([role, permissions]) => ({
    role: String(role || '').trim().toLowerCase(),
    permissionsCsv: normalizePermissionList(permissions).join(', '),
  }))
  .filter((row) => row.role)
  .sort((left, right) => left.role.localeCompare(right.role, 'ru-RU'));

export const mapRowsToRolePermissions = (rows) => {
  const result = {};
  for (const row of Array.isArray(rows) ? rows : []) {
    const role = String(row?.role || '').trim().toLowerCase();
    if (!role) continue;

    const permissions = Array.from(new Set(normalizePermissionList(row?.permissionsCsv)));
    if (!permissions.length) continue;

    result[role] = permissions;
  }

  return result;
};
