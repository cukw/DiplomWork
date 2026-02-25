import React, { useEffect, useRef, useState } from 'react';
import {
  Box,
  Card,
  CardContent,
  Grid,
  Typography,
  Paper,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Chip,
  CircularProgress,
  Alert,
  Stack,
  Button,
  Divider,
  LinearProgress,
  FormControlLabel,
  Switch,
  Select,
  MenuItem,
  FormControl,
  InputLabel,
} from '@mui/material';
import {
  Bolt,
  Refresh,
  WarningAmber,
  ShieldOutlined,
  AutoGraph,
  Timeline,
  Computer,
  NotificationsActive,
} from '@mui/icons-material';
import { alpha } from '@mui/material/styles';
import { useAuth } from '../contexts/AuthContext';
import { useNotifications } from '../contexts/NotificationContext';
import { dashboardAPI } from '../services/api';

const initialStats = {
  totalActivities: 0,
  blockedActivities: 0,
  anomalyCount: 0,
  averageRiskScore: 0,
  activityTypeCounts: {},
};

const formatDateTime = (value) => {
  if (!value) return 'N/A';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return String(value);

  return new Intl.DateTimeFormat('ru-RU', {
    day: '2-digit',
    month: 'short',
    hour: '2-digit',
    minute: '2-digit',
  }).format(date);
};

const titleCaseActivity = (value) => {
  if (!value) return 'Unknown';
  return String(value)
    .replace(/_/g, ' ')
    .toLowerCase()
    .replace(/\b\w/g, (m) => m.toUpperCase());
};

const normalizeStats = (data) => ({
  totalActivities: Number(data?.totalActivities ?? data?.total_activities ?? 0),
  blockedActivities: Number(data?.blockedActivities ?? data?.blocked_activities ?? 0),
  anomalyCount: Number(data?.anomalyCount ?? data?.anomaliesCount ?? data?.anomaly_count ?? 0),
  averageRiskScore: Number(data?.averageRiskScore ?? data?.average_risk_score ?? 0),
  activityTypeCounts: data?.activityTypeCounts ?? data?.activity_type_counts ?? {},
});

const normalizeActivity = (activity) => ({
  id: activity?.id,
  computerId: activity?.computerId ?? activity?.computer_id,
  timestamp: activity?.timestamp,
  activityType: activity?.activityType ?? activity?.activity_type,
  details: activity?.details ?? '',
  url: activity?.url ?? '',
  processName: activity?.processName ?? activity?.process_name ?? '',
  isBlocked: Boolean(activity?.isBlocked ?? activity?.is_blocked),
  riskScore: Number(activity?.riskScore ?? activity?.risk_score ?? 0),
});

const deriveAnomalySeverity = (anomaly) => {
  const haystack = `${anomaly?.type || ''} ${anomaly?.description || ''}`.toLowerCase();
  if (/unauthor|malware|exfil|critical|forbidden/.test(haystack)) return 'High';
  if (/suspicious|anomaly|blocked|risk/.test(haystack)) return 'Medium';
  return 'Low';
};

const normalizeAnomaly = (anomaly) => ({
  id: anomaly?.id,
  type: anomaly?.type || 'Anomaly',
  description: anomaly?.description || '',
  detectedAt: anomaly?.detectedAt ?? anomaly?.detected_at,
  activityId: anomaly?.activityId ?? anomaly?.activity_id,
  severity: deriveAnomalySeverity(anomaly),
});

const getSeverityColor = (severity) => {
  switch (severity) {
    case 'High':
      return 'error';
    case 'Medium':
      return 'warning';
    default:
      return 'info';
  }
};

const getActivityStatus = (activity) => {
  if (activity.isBlocked) return { label: 'Blocked', color: 'error' };
  if (activity.riskScore >= 10) return { label: 'Warning', color: 'warning' };
  return { label: 'Normal', color: 'success' };
};

const Dashboard = () => {
  const { user } = useAuth();
  const { unreadCount } = useNotifications();

  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState(null);
  const [sectionErrors, setSectionErrors] = useState({});
  const [lastUpdated, setLastUpdated] = useState(null);

  const [stats, setStats] = useState(initialStats);
  const [recentActivities, setRecentActivities] = useState([]);
  const [anomalies, setAnomalies] = useState([]);
  const [autoRefresh, setAutoRefresh] = useState(() => {
    const stored = localStorage.getItem('dashboard_auto_refresh');
    return stored == null ? true : stored === 'true';
  });
  const [refreshIntervalSec, setRefreshIntervalSec] = useState(() => {
    const stored = Number(localStorage.getItem('dashboard_refresh_interval_sec'));
    return Number.isFinite(stored) && stored >= 5 ? stored : 10;
  });
  const lastLiveRefreshAtRef = useRef(0);

  useEffect(() => {
    fetchDashboardData({ initial: true });
  }, []);

  useEffect(() => {
    localStorage.setItem('dashboard_auto_refresh', String(autoRefresh));
  }, [autoRefresh]);

  useEffect(() => {
    localStorage.setItem('dashboard_refresh_interval_sec', String(refreshIntervalSec));
  }, [refreshIntervalSec]);

  useEffect(() => {
    if (!autoRefresh) return undefined;

    const id = window.setInterval(() => {
      if (document.hidden) return;
      fetchDashboardData();
    }, refreshIntervalSec * 1000);

    return () => window.clearInterval(id);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [autoRefresh, refreshIntervalSec]);

  useEffect(() => {
    if (!autoRefresh) return undefined;

    const handleLiveUpdate = () => {
      if (document.hidden) return;

      const now = Date.now();
      const minGapMs = Math.max(3000, Math.floor((refreshIntervalSec * 1000) / 2));
      if (now - lastLiveRefreshAtRef.current < minGapMs) {
        return;
      }

      lastLiveRefreshAtRef.current = now;
      fetchDashboardData();
    };

    window.addEventListener('ams:live-update', handleLiveUpdate);
    return () => window.removeEventListener('ams:live-update', handleLiveUpdate);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [autoRefresh, refreshIntervalSec]);

  const fetchDashboardData = async ({ initial = false } = {}) => {
    try {
      if (initial) {
        setLoading(true);
      } else {
        setRefreshing(true);
      }

      setError(null);
      setSectionErrors({});

      const [statsResult, activitiesResult, anomaliesResult] = await Promise.allSettled([
        dashboardAPI.getStats(),
        dashboardAPI.getRecentActivities(),
        dashboardAPI.getRecentAnomalies(),
      ]);

      const nextSectionErrors = {};
      let successCount = 0;

      if (statsResult.status === 'fulfilled') {
        setStats(normalizeStats(statsResult.value));
        successCount += 1;
      } else {
        nextSectionErrors.stats = 'Не удалось загрузить сводную статистику.';
      }

      if (activitiesResult.status === 'fulfilled') {
        setRecentActivities((Array.isArray(activitiesResult.value) ? activitiesResult.value : []).map(normalizeActivity));
        successCount += 1;
      } else {
        nextSectionErrors.activities = 'Не удалось загрузить последние активности.';
      }

      if (anomaliesResult.status === 'fulfilled') {
        setAnomalies((Array.isArray(anomaliesResult.value) ? anomaliesResult.value : []).map(normalizeAnomaly));
        successCount += 1;
      } else {
        nextSectionErrors.anomalies = 'Не удалось загрузить аномалии.';
      }

      setSectionErrors(nextSectionErrors);

      if (successCount === 0) {
        setError('Не удалось загрузить данные дашборда.');
      } else {
        setLastUpdated(new Date());
      }
    } catch (err) {
      setError('Не удалось загрузить данные дашборда.');
      console.error('Dashboard error:', err);
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  };

  const activityTypeEntries = Object.entries(stats.activityTypeCounts || {})
    .map(([name, count]) => ({ name, label: titleCaseActivity(name), count: Number(count) || 0 }))
    .sort((a, b) => b.count - a.count);

  const topActivityType = activityTypeEntries[0];
  const maxTypeCount = activityTypeEntries[0]?.count || 1;
  const anomalyRate = stats.totalActivities > 0
    ? (stats.anomalyCount / stats.totalActivities) * 100
    : 0;

  if (loading) {
    return (
      <Box display="flex" justifyContent="center" alignItems="center" minHeight="56vh">
        <CircularProgress />
      </Box>
    );
  }

  return (
    <Box>
      <Stack
        direction={{ xs: 'column', md: 'row' }}
        justifyContent="space-between"
        alignItems={{ xs: 'flex-start', md: 'center' }}
        spacing={2}
        mb={3}
      >
        <Box>
          <Typography variant="h4" gutterBottom>
            Dashboard
          </Typography>
          <Typography variant="body1" color="text.secondary">
            Оперативный обзор активности, блокировок и аномалий.
          </Typography>
        </Box>
        <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
          <FormControl size="small" sx={{ minWidth: 120 }}>
            <InputLabel>Интервал</InputLabel>
            <Select
              label="Интервал"
              value={refreshIntervalSec}
              onChange={(e) => setRefreshIntervalSec(Number(e.target.value))}
              disabled={!autoRefresh}
            >
              {[5, 10, 15, 30, 60].map((seconds) => (
                <MenuItem key={seconds} value={seconds}>{seconds} сек</MenuItem>
              ))}
            </Select>
          </FormControl>
          <FormControlLabel
            sx={{ ml: 0 }}
            control={(
              <Switch
                checked={autoRefresh}
                onChange={(e) => setAutoRefresh(e.target.checked)}
              />
            )}
            label="Live"
          />
          {lastUpdated && (
            <Chip
              variant="outlined"
              label={`Обновлено: ${formatDateTime(lastUpdated.toISOString())}`}
              sx={{ maxWidth: 260 }}
            />
          )}
          <Button
            variant="contained"
            startIcon={<Refresh />}
            onClick={() => fetchDashboardData()}
            disabled={refreshing}
          >
            {refreshing ? 'Обновление...' : 'Обновить'}
          </Button>
        </Stack>
      </Stack>

      {refreshing && <LinearProgress sx={{ mb: 2, borderRadius: 999 }} />}

      {error && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {error}
        </Alert>
      )}

      <Card
        sx={(theme) => ({
          mb: 3,
          overflow: 'hidden',
          background:
            theme.palette.mode === 'dark'
              ? `linear-gradient(150deg, ${alpha(theme.palette.primary.main, 0.18)} 0%, ${alpha(theme.palette.info.main, 0.14)} 38%, ${alpha(theme.palette.background.paper, 0.92)} 100%)`
              : `linear-gradient(150deg, ${alpha(theme.palette.primary.main, 0.10)} 0%, ${alpha(theme.palette.info.main, 0.08)} 38%, ${alpha(theme.palette.common.white, 0.90)} 100%)`,
        })}
      >
        <CardContent sx={{ p: { xs: 2, md: 3 } }}>
          <Grid container spacing={2} alignItems="stretch">
            <Grid item xs={12} md={7}>
              <Typography variant="overline" color="primary" sx={{ letterSpacing: '0.10em', fontWeight: 700 }}>
                Live Operations
              </Typography>
              <Typography variant="h5" sx={{ mt: 0.5, mb: 1 }}>
                {user?.username ? `Добро пожаловать, ${user.username}` : 'Мониторинг активности включен'}
              </Typography>
              <Typography variant="body2" color="text.secondary" sx={{ maxWidth: 700, lineHeight: 1.65 }}>
                Основные сервисы запущены. Используйте панель ниже для контроля событий, аномалий и распределения
                типов активности. При необходимости обновите данные вручную.
              </Typography>

              <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.2} mt={2.2}>
                <Chip icon={<NotificationsActive />} label={`${unreadCount} непрочитанных уведомлений`} color="primary" variant="outlined" />
                <Chip icon={<ShieldOutlined />} label={`${stats.blockedActivities} заблокированных действий`} color="warning" variant="outlined" />
                <Chip icon={<WarningAmber />} label={`${stats.anomalyCount} аномалий`} color="error" variant="outlined" />
              </Stack>
            </Grid>

            <Grid item xs={12} md={5}>
              <Paper
                sx={(theme) => ({
                  p: 2,
                  height: '100%',
                  border: `1px solid ${alpha(theme.palette.primary.main, theme.palette.mode === 'dark' ? 0.16 : 0.08)}`,
                  backgroundColor: theme.palette.mode === 'dark'
                    ? alpha(theme.palette.background.paper, 0.88)
                    : alpha(theme.palette.common.white, 0.74),
                  boxShadow: theme.palette.mode === 'dark'
                    ? `0 12px 28px ${alpha(theme.palette.common.black, 0.26)}`
                    : 'none',
                })}
              >
                <Stack spacing={1.5}>
                  <Stack direction="row" justifyContent="space-between" alignItems="center">
                    <Typography variant="body2" color="text.secondary">
                      Top activity type
                    </Typography>
                    <Chip size="small" label={topActivityType?.label || 'No data'} color="info" />
                  </Stack>

                  <Divider />

                  <Grid container spacing={1}>
                    <Grid item xs={6}>
                      <Typography variant="caption" color="text.secondary">
                        Total events
                      </Typography>
                      <Typography variant="h6">{stats.totalActivities}</Typography>
                    </Grid>
                    <Grid item xs={6}>
                      <Typography variant="caption" color="text.secondary">
                        Anomaly rate
                      </Typography>
                      <Typography variant="h6">{anomalyRate.toFixed(1)}%</Typography>
                    </Grid>
                    <Grid item xs={6}>
                      <Typography variant="caption" color="text.secondary">
                        Avg risk score
                      </Typography>
                      <Typography variant="h6">{stats.averageRiskScore.toFixed(1)}</Typography>
                    </Grid>
                    <Grid item xs={6}>
                      <Typography variant="caption" color="text.secondary">
                        Activity categories
                      </Typography>
                      <Typography variant="h6">{activityTypeEntries.length}</Typography>
                    </Grid>
                  </Grid>
                </Stack>
              </Paper>
            </Grid>
          </Grid>
        </CardContent>
      </Card>

      <Grid container spacing={2.5} sx={{ mb: 3 }}>
        {[
          {
            title: 'Total Activities',
            value: stats.totalActivities,
            caption: 'Events processed',
            icon: <Timeline />,
            color: 'primary',
          },
          {
            title: 'Blocked Activities',
            value: stats.blockedActivities,
            caption: 'Policy enforcement',
            icon: <ShieldOutlined />,
            color: 'warning',
          },
          {
            title: 'Anomalies',
            value: stats.anomalyCount,
            caption: 'Requires review',
            icon: <WarningAmber />,
            color: 'error',
          },
          {
            title: 'Avg Risk Score',
            value: stats.averageRiskScore.toFixed(1),
            caption: 'Across all events',
            icon: <AutoGraph />,
            color: 'info',
          },
        ].map((item) => (
          <Grid item xs={12} sm={6} lg={3} key={item.title}>
            <Card sx={{ height: '100%' }}>
              <CardContent>
                <Stack direction="row" justifyContent="space-between" alignItems="flex-start">
                  <Box>
                    <Typography variant="body2" color="text.secondary">
                      {item.title}
                    </Typography>
                    <Typography variant="h4" sx={{ mt: 0.75 }}>
                      {item.value}
                    </Typography>
                    <Typography variant="caption" color="text.secondary">
                      {item.caption}
                    </Typography>
                  </Box>
                  <Box
                    sx={(theme) => ({
                      width: 42,
                      height: 42,
                      borderRadius: 2,
                      display: 'grid',
                      placeItems: 'center',
                      color: `${item.color}.main`,
                      backgroundColor: alpha(theme.palette[item.color].main, 0.10),
                    })}
                  >
                    {item.icon}
                  </Box>
                </Stack>
              </CardContent>
            </Card>
          </Grid>
        ))}
      </Grid>

      <Grid container spacing={2.5}>
        <Grid item xs={12} lg={4}>
          <Card sx={{ height: '100%' }}>
            <CardContent>
              <Typography variant="h6" gutterBottom>
                Activity Type Distribution
              </Typography>
              <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
                Распределение по категориям активности за текущий набор данных.
              </Typography>

              {sectionErrors.stats && (
                <Alert severity="warning" sx={{ mb: 2 }}>
                  {sectionErrors.stats}
                </Alert>
              )}

              {activityTypeEntries.length === 0 ? (
                <Typography variant="body2" color="text.secondary">
                  Данные о типах активности пока отсутствуют.
                </Typography>
              ) : (
                <Stack spacing={1.25}>
                  {activityTypeEntries.slice(0, 8).map((entry) => (
                    <Box key={entry.name}>
                      <Stack direction="row" justifyContent="space-between" mb={0.5}>
                        <Typography variant="body2" sx={{ fontWeight: 600 }}>
                          {entry.label}
                        </Typography>
                        <Typography variant="body2" color="text.secondary">
                          {entry.count}
                        </Typography>
                      </Stack>
                      <LinearProgress
                        variant="determinate"
                        value={(entry.count / maxTypeCount) * 100}
                        sx={{
                          height: 8,
                          borderRadius: 999,
                          backgroundColor: (theme) => alpha(theme.palette.primary.main, 0.08),
                          '& .MuiLinearProgress-bar': {
                            borderRadius: 999,
                          },
                        }}
                      />
                    </Box>
                  ))}
                </Stack>
              )}
            </CardContent>
          </Card>
        </Grid>

        <Grid item xs={12} lg={8}>
          <Card sx={{ height: '100%' }}>
            <CardContent>
              <Stack direction="row" justifyContent="space-between" alignItems="center" mb={1.5}>
                <Typography variant="h6">Recent Activities</Typography>
                <Chip size="small" icon={<Bolt />} label={`${recentActivities.length} records`} />
              </Stack>

              {sectionErrors.activities && (
                <Alert severity="warning" sx={{ mb: 2 }}>
                  {sectionErrors.activities}
                </Alert>
              )}

              <TableContainer sx={{ maxHeight: 420 }}>
                <Table stickyHeader size="small">
                  <TableHead>
                    <TableRow>
                      <TableCell>Type</TableCell>
                      <TableCell>Computer</TableCell>
                      <TableCell>Context</TableCell>
                      <TableCell align="right">Risk</TableCell>
                      <TableCell>Time</TableCell>
                      <TableCell>Status</TableCell>
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {recentActivities.length === 0 ? (
                      <TableRow>
                        <TableCell colSpan={6}>
                          <Typography variant="body2" color="text.secondary">
                            Нет данных по активности.
                          </Typography>
                        </TableCell>
                      </TableRow>
                    ) : (
                      recentActivities.map((activity) => {
                        const status = getActivityStatus(activity);
                        const contextText =
                          activity.processName ||
                          activity.url ||
                          activity.details ||
                          '—';

                        return (
                          <TableRow key={activity.id}>
                            <TableCell>
                              <Stack spacing={0.25}>
                                <Typography variant="body2" sx={{ fontWeight: 600 }}>
                                  {titleCaseActivity(activity.activityType)}
                                </Typography>
                                <Typography variant="caption" color="text.secondary">
                                  #{activity.id}
                                </Typography>
                              </Stack>
                            </TableCell>
                            <TableCell>
                              <Stack direction="row" spacing={0.5} alignItems="center">
                                <Computer sx={{ fontSize: 16, color: 'text.secondary' }} />
                                <Typography variant="body2">{activity.computerId ?? 'N/A'}</Typography>
                              </Stack>
                            </TableCell>
                            <TableCell sx={{ maxWidth: 320 }}>
                              <Typography
                                variant="body2"
                                sx={{
                                  overflow: 'hidden',
                                  textOverflow: 'ellipsis',
                                  whiteSpace: 'nowrap',
                                }}
                                title={contextText}
                              >
                                {contextText}
                              </Typography>
                            </TableCell>
                            <TableCell align="right">
                              <Typography
                                variant="body2"
                                sx={{
                                  fontWeight: 700,
                                  color:
                                    activity.riskScore >= 10
                                      ? 'error.main'
                                      : activity.riskScore >= 7
                                        ? 'warning.main'
                                        : 'text.primary',
                                }}
                              >
                                {activity.riskScore.toFixed(1)}
                              </Typography>
                            </TableCell>
                            <TableCell>{formatDateTime(activity.timestamp)}</TableCell>
                            <TableCell>
                              <Chip size="small" label={status.label} color={status.color} />
                            </TableCell>
                          </TableRow>
                        );
                      })
                    )}
                  </TableBody>
                </Table>
              </TableContainer>
            </CardContent>
          </Card>
        </Grid>

        <Grid item xs={12}>
          <Card>
            <CardContent>
              <Stack direction="row" justifyContent="space-between" alignItems="center" mb={1.5}>
                <Typography variant="h6">Recent Anomalies</Typography>
                <Chip size="small" color="error" label={`${anomalies.length} entries`} />
              </Stack>

              {sectionErrors.anomalies && (
                <Alert severity="warning" sx={{ mb: 2 }}>
                  {sectionErrors.anomalies}
                </Alert>
              )}

              {anomalies.length === 0 ? (
                <Typography variant="body2" color="text.secondary">
                  Аномалии не обнаружены или данные ещё не загружены.
                </Typography>
              ) : (
                <Grid container spacing={2}>
                  {anomalies.slice(0, 6).map((anomaly) => (
                    <Grid item xs={12} md={6} xl={4} key={anomaly.id}>
                      <Paper
                        sx={(theme) => ({
                          p: 2,
                          height: '100%',
                          border: `1px solid ${alpha(theme.palette.error.main, 0.12)}`,
                          background:
                            theme.palette.mode === 'dark'
                              ? `linear-gradient(180deg, ${alpha(theme.palette.error.main, 0.08)} 0%, ${alpha(theme.palette.background.paper, 0.92)} 100%)`
                              : `linear-gradient(180deg, ${alpha(theme.palette.error.main, 0.04)} 0%, ${alpha(theme.palette.common.white, 0.88)} 100%)`,
                        })}
                      >
                        <Stack direction="row" justifyContent="space-between" alignItems="flex-start" spacing={1}>
                          <Typography variant="subtitle2" sx={{ fontWeight: 700 }}>
                            {anomaly.type}
                          </Typography>
                          <Chip
                            size="small"
                            color={getSeverityColor(anomaly.severity)}
                            label={anomaly.severity}
                          />
                        </Stack>

                        <Typography
                          variant="body2"
                          color="text.secondary"
                          sx={{ mt: 1, minHeight: 42 }}
                        >
                          {anomaly.description || 'No description provided'}
                        </Typography>

                        <Divider sx={{ my: 1.25 }} />

                        <Stack direction="row" justifyContent="space-between">
                          <Typography variant="caption" color="text.secondary">
                            Activity #{anomaly.activityId ?? 'N/A'}
                          </Typography>
                          <Typography variant="caption" color="text.secondary">
                            {formatDateTime(anomaly.detectedAt)}
                          </Typography>
                        </Stack>
                      </Paper>
                    </Grid>
                  ))}
                </Grid>
              )}
            </CardContent>
          </Card>
        </Grid>
      </Grid>
    </Box>
  );
};

export default Dashboard;
