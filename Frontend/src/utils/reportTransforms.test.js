import {
  normalizeActivityTypeKey,
  buildActivityTypes,
  buildTimelineFromActivities,
  filterActivitiesByDateRange,
} from './reportTransforms';

describe('reportTransforms', () => {
  it('normalizes activity type key', () => {
    expect(normalizeActivityTypeKey('file access')).toBe('FILE_ACCESS');
    expect(normalizeActivityTypeKey('network-access')).toBe('NETWORK_ACCESS');
    expect(normalizeActivityTypeKey('')).toBe('UNKNOWN');
  });

  it('merges activity type counters with normalization', () => {
    const result = buildActivityTypes({
      'file access': 2,
      FILE_ACCESS: 3,
      'network-access': 1,
    });

    expect(result).toEqual([
      { name: 'FILE_ACCESS', count: 5 },
      { name: 'NETWORK_ACCESS', count: 1 },
    ]);
  });

  it('builds timeline with blocked and anomaly counters', () => {
    const timeline = buildTimelineFromActivities([
      { timestamp: '2026-03-17T10:00:00Z', isBlocked: true, riskScore: 80 },
      { timestamp: '2026-03-17T11:00:00Z', isBlocked: false, riskScore: 40 },
      { timestamp: '2026-03-18T10:00:00Z', isBlocked: false, riskScore: 20 },
    ]);

    expect(timeline).toEqual([
      { date: '2026-03-17', count: 2, blocked: 1, anomalies: 1, riskScore: 60 },
      { date: '2026-03-18', count: 1, blocked: 0, anomalies: 0, riskScore: 20 },
    ]);
  });

  it('filters activities by inclusive date range', () => {
    const filtered = filterActivitiesByDateRange(
      [
        { timestamp: '2026-03-10T23:00:00Z' },
        { timestamp: '2026-03-11T08:00:00Z' },
        { timestamp: '2026-03-12T20:00:00Z' },
      ],
      '2026-03-11',
      '2026-03-12'
    );

    expect(filtered).toHaveLength(2);
    expect(filtered[0].timestamp).toBe('2026-03-11T08:00:00Z');
    expect(filtered[1].timestamp).toBe('2026-03-12T20:00:00Z');
  });
});
