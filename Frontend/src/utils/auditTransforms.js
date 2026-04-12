export const formatAuditDetailsPreview = (raw, maxLength = 140) => {
  if (!raw) return '—';
  if (typeof raw !== 'string') return String(raw);

  try {
    const parsed = JSON.parse(raw);
    const compact = JSON.stringify(parsed);
    return compact.length > maxLength ? `${compact.slice(0, maxLength - 3)}...` : compact;
  } catch {
    return raw.length > maxLength ? `${raw.slice(0, maxLength - 3)}...` : raw;
  }
};

export const escapeCsvValue = (value) => {
  if (value === null || value === undefined) return '';
  return `"${String(value).replaceAll('"', '""')}"`;
};

export const buildAuditCsv = (events = []) => {
  const header = [
    'id',
    'createdAt',
    'action',
    'actor',
    'targetType',
    'targetId',
    'success',
    'statusCode',
    'detailsJson',
  ].join(',');

  const body = (Array.isArray(events) ? events : []).map((item) => ([
    escapeCsvValue(item?.id),
    escapeCsvValue(item?.createdAt),
    escapeCsvValue(item?.action),
    escapeCsvValue(item?.actor),
    escapeCsvValue(item?.targetType),
    escapeCsvValue(item?.targetId),
    escapeCsvValue(item?.success),
    escapeCsvValue(item?.statusCode),
    escapeCsvValue(item?.detailsJson),
  ].join(',')));

  return [header, ...body].join('\n');
};
