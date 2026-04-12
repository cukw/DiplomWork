import {
  normalizePermissionList,
  mapRolePermissionsToRows,
  mapRowsToRolePermissions,
} from './rbacTransforms';

describe('rbacTransforms', () => {
  it('normalizes permission list from string input', () => {
    expect(normalizePermissionList('dashboard.*, audit.getEvents; reports.*\n reports.*')).toEqual([
      'dashboard.*',
      'audit.getevents',
      'reports.*',
      'reports.*',
    ]);
  });

  it('maps role permissions object to editable rows', () => {
    const rows = mapRolePermissionsToRows({
      Admin: ['*'],
      auditor: ['audit.getevents', 'reports.*'],
    });

    expect(rows).toEqual([
      { role: 'admin', permissionsCsv: '*' },
      { role: 'auditor', permissionsCsv: 'audit.getevents, reports.*' },
    ]);
  });

  it('maps editable rows back to role permission object with dedupe', () => {
    const matrix = mapRowsToRolePermissions([
      { role: 'Admin', permissionsCsv: '*, dashboard.* , *' },
      { role: 'auditor', permissionsCsv: 'audit.getEvents, reports.*' },
      { role: '', permissionsCsv: 'ignored' },
      { role: 'empty', permissionsCsv: '' },
    ]);

    expect(matrix).toEqual({
      admin: ['*', 'dashboard.*'],
      auditor: ['audit.getevents', 'reports.*'],
    });
  });
});
