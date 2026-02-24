const toDateKey = (value) => {
  if (!value) return '';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return String(value).slice(0, 10);
  }
  return date.toISOString().slice(0, 10);
};

const toMillis = (value) => {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? null : date.getTime();
};

const toNumber = (value, fallback = 0) => {
  const number = Number(value);
  return Number.isFinite(number) ? number : fallback;
};

export const normalizeActivityTypeKey = (value) => {
  if (!value) return 'UNKNOWN';
  return String(value)
    .trim()
    .replace(/[-\s]+/g, '_')
    .toUpperCase();
};

export const formatDateInput = (date) => {
  const d = new Date(date);
  if (Number.isNaN(d.getTime())) return '';
  return d.toISOString().slice(0, 10);
};

export const addDays = (dateString, days) => {
  const d = new Date(`${dateString}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + days);
  return d.toISOString().slice(0, 10);
};

export const getMonthBounds = (date = new Date()) => {
  const d = new Date(date);
  const start = new Date(Date.UTC(d.getUTCFullYear(), d.getUTCMonth(), 1));
  const end = new Date(Date.UTC(d.getUTCFullYear(), d.getUTCMonth() + 1, 0));
  return {
    startDate: start.toISOString().slice(0, 10),
    endDate: end.toISOString().slice(0, 10),
  };
};

export const filterActivitiesByDateRange = (activities = [], startDate, endDate) => {
  if (!startDate && !endDate) return activities;

  const startMs = startDate ? Date.parse(`${startDate}T00:00:00Z`) : null;
  const endMs = endDate ? Date.parse(`${endDate}T23:59:59.999Z`) : null;

  return activities.filter((activity) => {
    const ts = toMillis(activity?.timestamp);
    if (ts == null) return false;
    if (startMs != null && ts < startMs) return false;
    if (endMs != null && ts > endMs) return false;
    return true;
  });
};

export const buildTimelineFromActivities = (activities = [], groupBy = 'day') => {
  const buckets = new Map();

  activities.forEach((activity) => {
    const rawTs = activity?.timestamp;
    const date = new Date(rawTs);
    if (Number.isNaN(date.getTime())) return;

    let key;
    if (groupBy === 'hour') {
      key = `${date.toISOString().slice(0, 13)}:00`;
    } else {
      key = toDateKey(rawTs);
    }

    const bucket = buckets.get(key) || {
      date: key,
      count: 0,
      blocked: 0,
      riskScoreSum: 0,
      riskScoreCount: 0,
      anomalies: 0,
    };

    bucket.count += 1;
    if (activity?.isBlocked) bucket.blocked += 1;

    const riskScore = toNumber(activity?.riskScore, Number.NaN);
    if (Number.isFinite(riskScore)) {
      bucket.riskScoreSum += riskScore;
      bucket.riskScoreCount += 1;
      if (riskScore >= 70) bucket.anomalies += 1;
    }

    buckets.set(key, bucket);
  });

  return Array.from(buckets.values())
    .sort((a, b) => String(a.date).localeCompare(String(b.date)))
    .map((bucket) => ({
      date: bucket.date,
      count: bucket.count,
      blocked: bucket.blocked,
      anomalies: bucket.anomalies,
      riskScore: bucket.riskScoreCount
        ? Number((bucket.riskScoreSum / bucket.riskScoreCount).toFixed(2))
        : 0,
    }));
};

export const buildActivityTypes = (activityTypeCounts) => {
  const merged = new Map();

  Object.entries(activityTypeCounts || {}).forEach(([rawName, count]) => {
    const name = normalizeActivityTypeKey(rawName);
    merged.set(name, (merged.get(name) || 0) + toNumber(count));
  });

  return Array.from(merged.entries())
    .map(([name, count]) => ({ name, count }))
    .sort((a, b) => b.count - a.count);
};

export const buildTopComputers = (activities = [], limit = 10) => {
  const byComputer = new Map();

  activities.forEach((activity) => {
    const key = activity?.computerId ?? 'unknown';
    const entry = byComputer.get(key) || {
      computerId: key,
      computerName: `PC-${key}`,
      count: 0,
      blocked: 0,
      _riskSum: 0,
      _riskCount: 0,
    };

    entry.count += 1;
    if (activity?.isBlocked) entry.blocked += 1;

    const riskScore = toNumber(activity?.riskScore, Number.NaN);
    if (Number.isFinite(riskScore)) {
      entry._riskSum += riskScore;
      entry._riskCount += 1;
    }

    byComputer.set(key, entry);
  });

  return Array.from(byComputer.values())
    .map((entry) => ({
      computerId: entry.computerId,
      computerName: entry.computerName,
      count: entry.count,
      blocked: entry.blocked,
      avgRiskScore: entry._riskCount
        ? Number((entry._riskSum / entry._riskCount).toFixed(2))
        : 0,
    }))
    .sort((a, b) => b.count - a.count)
    .slice(0, limit);
};

export const buildAnomalyTypes = (anomalies = [], activityIdSet = null) => {
  const counts = new Map();

  anomalies.forEach((anomaly) => {
    if (activityIdSet && activityIdSet.size > 0 && !activityIdSet.has(anomaly?.activityId)) {
      return;
    }

    const type = anomaly?.type || 'UNKNOWN';
    counts.set(type, (counts.get(type) || 0) + 1);
  });

  return Array.from(counts.entries())
    .map(([type, count]) => ({ type, count }))
    .sort((a, b) => b.count - a.count);
};

export const normalizeActivityReport = ({ rawReport, anomalies = [], startDate, endDate, groupBy = 'day' }) => {
  const activities = filterActivitiesByDateRange(rawReport?.activities || [], startDate, endDate);
  const activityIdSet = new Set(activities.map((activity) => activity.id));

  const timeline = buildTimelineFromActivities(activities, groupBy);
  const activityTypes = buildActivityTypes(rawReport?.activityTypeCounts);
  const topComputers = buildTopComputers(activities);
  const anomalyTypes = buildAnomalyTypes(anomalies, activityIdSet);
  const topProcesses = buildTopFieldValues(activities, (activity) => activity?.processName, { key: 'processName' });
  const topUrls = buildTopFieldValues(activities, (activity) => activity?.url, { key: 'url' });

  return {
    range: {
      startDate: rawReport?.startDate || rawReport?.date || startDate || null,
      endDate: rawReport?.endDate || rawReport?.date || endDate || null,
      groupBy,
    },
    summary: {
      totalActivities: toNumber(rawReport?.totalActivities),
      totalAnomalies: toNumber(rawReport?.anomalyCount),
      blockedActivities: toNumber(rawReport?.blockedActivities),
      averageRiskScore: toNumber(rawReport?.averageRiskScore),
    },
    timeline,
    activityTypes,
    anomalyTypes,
    topComputers,
    topProcesses,
    topUrls,
    anomalies,
    activities,
    raw: rawReport,
  };
};

export const filterActivitiesByTimelineBucket = (activities = [], bucketValue = '', groupBy = 'day') => {
  if (!bucketValue) return activities;
  const normalizedBucket = String(bucketValue);

  return activities.filter((activity) => {
    const ts = activity?.timestamp;
    if (!ts) return false;
    const date = new Date(ts);
    if (Number.isNaN(date.getTime())) return false;

    if (groupBy === 'hour') {
      return `${date.toISOString().slice(0, 13)}:00` === normalizedBucket;
    }
    return date.toISOString().slice(0, 10) === normalizedBucket;
  });
};

const buildTopFieldValues = (activities = [], selector, { key }) => {
  const counts = new Map();

  activities.forEach((activity) => {
    const raw = selector(activity);
    if (!raw) return;

    const value = String(raw).trim();
    if (!value) return;
    const item = counts.get(value) || { [key]: value, count: 0 };
    item.count += 1;
    counts.set(value, item);
  });

  return Array.from(counts.values())
    .sort((a, b) => b.count - a.count)
    .slice(0, 10);
};

export const aggregateByDepartment = ({ users = [], activities = [], anomalies = [] }) => {
  const computerToDept = new Map();
  const departmentUsers = new Map();

  users.forEach((user) => {
    const department = user?.department || 'Unassigned';
    departmentUsers.set(department, (departmentUsers.get(department) || 0) + 1);
    if (user?.computer?.id != null) {
      computerToDept.set(user.computer.id, department);
    }
  });

  const rowsMap = new Map();
  const activityMap = new Map();

  activities.forEach((activity) => {
    activityMap.set(activity.id, activity);
    const department = computerToDept.get(activity?.computerId) || 'Unassigned';
    const row = rowsMap.get(department) || { department, activities: 0, anomalies: 0, users: 0 };
    row.activities += 1;
    rowsMap.set(department, row);
  });

  anomalies.forEach((anomaly) => {
    const activity = activityMap.get(anomaly?.activityId);
    if (!activity) return;
    const department = computerToDept.get(activity?.computerId) || 'Unassigned';
    const row = rowsMap.get(department) || { department, activities: 0, anomalies: 0, users: 0 };
    row.anomalies += 1;
    rowsMap.set(department, row);
  });

  departmentUsers.forEach((usersCount, department) => {
    const row = rowsMap.get(department) || { department, activities: 0, anomalies: 0, users: 0 };
    row.users = usersCount;
    rowsMap.set(department, row);
  });

  return Array.from(rowsMap.values()).sort((a, b) => b.activities - a.activities);
};

export const aggregateByUser = ({ users = [], activities = [] }) => {
  const computerToUser = new Map();
  const rowsMap = new Map();

  users.forEach((user) => {
    const id = user?.id;
    if (id == null) return;

    const name = user?.fullName || `User ${id}`;
    rowsMap.set(id, {
      id,
      name,
      department: user?.department || 'Unassigned',
      activities: 0,
      blocked: 0,
      avgRiskScore: 0,
      _riskSum: 0,
      _riskCount: 0,
    });

    if (user?.computer?.id != null) {
      computerToUser.set(user.computer.id, id);
    }
  });

  activities.forEach((activity) => {
    const userId = computerToUser.get(activity?.computerId);
    if (userId == null) return;

    const row = rowsMap.get(userId);
    if (!row) return;

    row.activities += 1;
    if (activity?.isBlocked) row.blocked += 1;

    const riskScore = toNumber(activity?.riskScore, Number.NaN);
    if (Number.isFinite(riskScore)) {
      row._riskSum += riskScore;
      row._riskCount += 1;
    }
  });

  return Array.from(rowsMap.values())
    .map((row) => ({
      ...row,
      avgRiskScore: row._riskCount ? Number((row._riskSum / row._riskCount).toFixed(2)) : 0,
    }))
    .sort((a, b) => b.activities - a.activities)
    .slice(0, 12);
};

export const compareSummaries = (currentSummary = {}, previousSummary = {}) => ({
  totalActivitiesDelta: toNumber(currentSummary.totalActivities) - toNumber(previousSummary.totalActivities),
  blockedActivitiesDelta: toNumber(currentSummary.blockedActivities) - toNumber(previousSummary.blockedActivities),
  totalAnomaliesDelta: toNumber(currentSummary.totalAnomalies) - toNumber(previousSummary.totalAnomalies),
  averageRiskScoreDelta: Number(
    (toNumber(currentSummary.averageRiskScore) - toNumber(previousSummary.averageRiskScore)).toFixed(2)
  ),
});
