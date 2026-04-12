import {
  formatAuditDetailsPreview,
  escapeCsvValue,
  buildAuditCsv,
} from './auditTransforms';

describe('auditTransforms', () => {
  it('formats details preview for empty and non-string values', () => {
    expect(formatAuditDetailsPreview(null)).toBe('—');
    expect(formatAuditDetailsPreview(undefined)).toBe('—');
    expect(formatAuditDetailsPreview(123)).toBe('123');
    expect(formatAuditDetailsPreview(false)).toBe('—');
  });

  it('formats compact JSON preview when raw string is valid JSON', () => {
    const preview = formatAuditDetailsPreview('{"a":1,"b":"x"}');
    expect(preview).toBe('{"a":1,"b":"x"}');
  });

  it('truncates long details preview with ellipsis', () => {
    const raw = 'x'.repeat(200);
    const preview = formatAuditDetailsPreview(raw, 20);
    expect(preview).toBe(`${'x'.repeat(17)}...`);
  });

  it('escapes csv values safely', () => {
    expect(escapeCsvValue(null)).toBe('');
    expect(escapeCsvValue(undefined)).toBe('');
    expect(escapeCsvValue('a,b')).toBe('"a,b"');
    expect(escapeCsvValue('a "quote"')).toBe('"a ""quote"""');
    expect(escapeCsvValue(true)).toBe('"true"');
  });

  it('builds csv with header and escaped rows', () => {
    const csv = buildAuditCsv([
      {
        id: 1,
        createdAt: '2026-03-19T10:00:00Z',
        action: 'agent.command.create',
        actor: 'admin',
        targetType: 'agent',
        targetId: '77',
        success: true,
        statusCode: 200,
        detailsJson: '{"x":"a,b"}',
      },
    ]);

    const lines = csv.split('\n');
    expect(lines).toHaveLength(2);
    expect(lines[0]).toContain('id,createdAt,action');
    expect(lines[1]).toContain('"1"');
    expect(lines[1]).toContain('"agent.command.create"');
    expect(lines[1]).toContain('"{""x"":""a,b""}"');
  });

  it('builds csv with only header for invalid input', () => {
    expect(buildAuditCsv(null)).toBe('id,createdAt,action,actor,targetType,targetId,success,statusCode,detailsJson');
    expect(buildAuditCsv({})).toBe('id,createdAt,action,actor,targetType,targetId,success,statusCode,detailsJson');
  });
});
