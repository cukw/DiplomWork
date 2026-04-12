import {
  formatDateInput,
  addDays,
  getMonthBounds,
  buildTopComputers,
  buildAnomalyTypes,
  normalizeActivityReport,
  filterActivitiesByTimelineBucket,
  aggregateByDepartment,
  aggregateByUser,
  compareSummaries,
  filterActivitiesByDateRange,
  buildTimelineFromActivities,
} from './reportTransforms';

describe('reportTransforms extra coverage', () => {
  it('formats date helpers', () => {
    expect(formatDateInput('2026-03-19T12:00:00Z')).toBe('2026-03-19');
    expect(formatDateInput('not-a-date')).toBe('');
    expect(addDays('2026-03-19', 2)).toBe('2026-03-21');

    const bounds = getMonthBounds('2026-02-12T10:00:00Z');
    expect(bounds).toEqual({ startDate: '2026-02-01', endDate: '2026-02-28' });
  });

  it('handles date range without boundaries and skips invalid timestamps', () => {
    const source = [{ timestamp: 'bad-date' }, { timestamp: '2026-03-20T00:00:00Z' }];
    expect(filterActivitiesByDateRange(source)).toBe(source);

    const filtered = filterActivitiesByDateRange(source, '2026-03-19', '2026-03-21');
    expect(filtered).toHaveLength(1);
    expect(filtered[0].timestamp).toBe('2026-03-20T00:00:00Z');
  });

  it('builds hourly timeline and ignores invalid rows', () => {
    const timeline = buildTimelineFromActivities(
      [
        { timestamp: '2026-03-19T10:10:00Z', isBlocked: false, riskScore: 10 },
        { timestamp: '2026-03-19T10:40:00Z', isBlocked: true, riskScore: 90 },
        { timestamp: 'invalid', isBlocked: true, riskScore: 100 },
      ],
      'hour'
    );

    expect(timeline).toEqual([
      { date: '2026-03-19T10:00', count: 2, blocked: 1, anomalies: 1, riskScore: 50 },
    ]);
  });

  it('builds top computers with avg risk and limit', () => {
    const top = buildTopComputers(
      [
        { computerId: 1, isBlocked: true, riskScore: 80 },
        { computerId: 1, isBlocked: false, riskScore: 20 },
        { computerId: 2, isBlocked: false, riskScore: 'invalid' },
      ],
      1
    );

    expect(top).toHaveLength(1);
    expect(top[0]).toEqual({
      computerId: 1,
      computerName: 'PC-1',
      count: 2,
      blocked: 1,
      avgRiskScore: 50,
    });
  });

  it('builds anomaly types with activity filter set', () => {
    const anomalies = [
      { activityId: 1, type: 'RISK_SPIKE' },
      { activityId: 2, type: 'RISK_SPIKE' },
      { activityId: 3, type: 'IDLE' },
      { activityId: 4 },
    ];

    const filtered = buildAnomalyTypes(anomalies, new Set([1, 3, 4]));
    expect(filtered).toEqual([
      { type: 'RISK_SPIKE', count: 1 },
      { type: 'IDLE', count: 1 },
      { type: 'UNKNOWN', count: 1 },
    ]);
  });

  it('normalizes full activity report with derived top lists', () => {
    const rawReport = {
      startDate: '2026-03-01',
      endDate: '2026-03-31',
      totalActivities: '3',
      anomalyCount: '2',
      blockedActivities: '1',
      averageRiskScore: '42.5',
      activityTypeCounts: {
        'site visit': 2,
        SITE_VISIT: 1,
      },
      activities: [
        { id: 11, timestamp: '2026-03-10T10:00:00Z', computerId: 7, processName: 'chrome', url: 'https://a', riskScore: 80, isBlocked: true },
        { id: 12, timestamp: '2026-03-10T11:00:00Z', computerId: 7, processName: 'chrome', url: 'https://b', riskScore: 10, isBlocked: false },
        { id: 13, timestamp: '2026-03-11T09:00:00Z', computerId: 8, processName: 'explorer', url: 'https://a', riskScore: 20, isBlocked: false },
      ],
    };

    const anomalies = [{ activityId: 11, type: 'RISK_SPIKE' }, { activityId: 12, type: 'MANUAL' }];
    const report = normalizeActivityReport({ rawReport, anomalies, groupBy: 'day' });

    expect(report.summary).toEqual({
      totalActivities: 3,
      totalAnomalies: 2,
      blockedActivities: 1,
      averageRiskScore: 42.5,
    });
    expect(report.activityTypes).toEqual([{ name: 'SITE_VISIT', count: 3 }]);
    expect(report.topProcesses[0]).toEqual({ processName: 'chrome', count: 2 });
    expect(report.topUrls[0]).toEqual({ url: 'https://a', count: 2 });
    expect(report.anomalyTypes).toEqual([
      { type: 'RISK_SPIKE', count: 1 },
      { type: 'MANUAL', count: 1 },
    ]);
  });

  it('filters activities by timeline bucket for day and hour', () => {
    const activities = [
      { timestamp: '2026-03-19T10:00:00Z' },
      { timestamp: '2026-03-19T10:30:00Z' },
      { timestamp: '2026-03-20T11:00:00Z' },
      { timestamp: null },
    ];

    expect(filterActivitiesByTimelineBucket(activities, '')).toHaveLength(4);
    expect(filterActivitiesByTimelineBucket(activities, '2026-03-19', 'day')).toHaveLength(2);
    expect(filterActivitiesByTimelineBucket(activities, '2026-03-19T10:00', 'hour')).toHaveLength(2);
  });

  it('aggregates by department and user', () => {
    const users = [
      { id: 1, fullName: 'Alice', department: 'IT', computer: { id: 101 } },
      { id: 2, fullName: 'Bob', department: 'HR', computer: { id: 102 } },
      { id: 3, fullName: 'NoPC', department: null, computer: null },
    ];
    const activities = [
      { id: 1001, computerId: 101, isBlocked: true, riskScore: 90 },
      { id: 1002, computerId: 101, isBlocked: false, riskScore: 10 },
      { id: 1003, computerId: 102, isBlocked: false, riskScore: 50 },
      { id: 1004, computerId: 999, isBlocked: false, riskScore: 0 },
    ];
    const anomalies = [
      { activityId: 1001, type: 'RISK' },
      { activityId: 1004, type: 'RISK' },
      { activityId: 9999, type: 'RISK' },
    ];

    const byDept = aggregateByDepartment({ users, activities, anomalies });
    const itRow = byDept.find((row) => row.department === 'IT');
    const hrRow = byDept.find((row) => row.department === 'HR');
    const unassignedRow = byDept.find((row) => row.department === 'Unassigned');

    expect(itRow).toMatchObject({ activities: 2, anomalies: 1, users: 1 });
    expect(hrRow).toMatchObject({ activities: 1, anomalies: 0, users: 1 });
    expect(unassignedRow).toMatchObject({ activities: 1, anomalies: 1, users: 1 });

    const byUser = aggregateByUser({ users, activities });
    expect(byUser.find((row) => row.id === 1)).toMatchObject({
      name: 'Alice',
      activities: 2,
      blocked: 1,
      avgRiskScore: 50,
    });
    expect(byUser.find((row) => row.id === 2)).toMatchObject({
      name: 'Bob',
      activities: 1,
      blocked: 0,
      avgRiskScore: 50,
    });
  });

  it('calculates summary deltas', () => {
    expect(compareSummaries(
      { totalActivities: 20, blockedActivities: 5, totalAnomalies: 7, averageRiskScore: 41.125 },
      { totalActivities: 10, blockedActivities: 3, totalAnomalies: 2, averageRiskScore: 40.1 }
    )).toEqual({
      totalActivitiesDelta: 10,
      blockedActivitiesDelta: 2,
      totalAnomaliesDelta: 5,
      averageRiskScoreDelta: 1.02,
    });
  });
});
