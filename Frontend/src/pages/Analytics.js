import React, { useEffect, useMemo, useState } from 'react';
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  Chip,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControl,
  FormControlLabel,
  Grid,
  InputLabel,
  MenuItem,
  Paper,
  Select,
  Stack,
  Switch,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  TextField,
  Typography,
} from '@mui/material';
import {
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
  Legend,
  Line,
  LineChart,
  Pie,
  PieChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts';
import { activityAPI, reportsAPI } from '../services/api';
import {
  addDays,
  filterActivitiesByTimelineBucket,
  formatDateInput,
  getMonthBounds,
  normalizeActivityReport,
} from '../utils/reportTransforms';

const COLORS = ['#0f766e', '#0369a1', '#d97706', '#b91c1c', '#7c3aed', '#15803d'];
const ANALYTICS_PRESETS_KEY = 'analytics_presets_v1';
const ANALYTICS_LIVE_KEY = 'analytics_auto_refresh';
const ANALYTICS_INTERVAL_KEY = 'analytics_refresh_interval_sec';

const readPresets = () => {
  try {
    const raw = localStorage.getItem(ANALYTICS_PRESETS_KEY);
    const parsed = raw ? JSON.parse(raw) : [];
    return Array.isArray(parsed) ? parsed : [];
  } catch {
    return [];
  }
};

const getRequestConfig = (reportType, date, startDate, endDate) => {
  if (reportType === 'daily') {
    return {
      startDate: date,
      endDate: date,
      groupBy: 'hour',
      fetchReport: () => reportsAPI.getDailyReport(date),
    };
  }

  if (reportType === 'weekly') {
    const weeklyEndDate = addDays(startDate, 6);
    return {
      startDate,
      endDate: weeklyEndDate,
      groupBy: 'day',
      fetchReport: () => reportsAPI.getCustomReport(startDate, weeklyEndDate, { groupBy: 'day' }),
    };
  }

  if (reportType === 'monthly') {
    const monthBounds = getMonthBounds(new Date());
    return {
      startDate: monthBounds.startDate,
      endDate: monthBounds.endDate,
      groupBy: 'day',
      fetchReport: () => reportsAPI.getCustomReport(monthBounds.startDate, monthBounds.endDate, { groupBy: 'day' }),
    };
  }

  return {
    startDate,
    endDate,
    groupBy: 'day',
    fetchReport: () => reportsAPI.getCustomReport(startDate, endDate, { groupBy: 'day' }),
  };
};

const Analytics = () => {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [reportType, setReportType] = useState('daily');
  const [reportData, setReportData] = useState(null);
  const [date, setDate] = useState(formatDateInput(new Date()));
  const [startDate, setStartDate] = useState(formatDateInput(new Date(Date.now() - 7 * 24 * 60 * 60 * 1000)));
  const [endDate, setEndDate] = useState(formatDateInput(new Date()));
  const [lastUpdated, setLastUpdated] = useState(null);
  const [autoRefresh, setAutoRefresh] = useState(() => {
    const raw = localStorage.getItem(ANALYTICS_LIVE_KEY);
    return raw == null ? false : raw === 'true';
  });
  const [refreshIntervalSec, setRefreshIntervalSec] = useState(() => {
    const parsed = Number(localStorage.getItem(ANALYTICS_INTERVAL_KEY));
    return Number.isFinite(parsed) && parsed >= 5 ? parsed : 15;
  });
  const [presets, setPresets] = useState(readPresets);
  const [selectedPresetId, setSelectedPresetId] = useState('');
  const [drilldown, setDrilldown] = useState({
    open: false,
    title: '',
    subtitle: '',
    rows: [],
  });

  const fetchReportData = async () => {
    try {
      setLoading(true);
      setError(null);

      const requestConfig = getRequestConfig(reportType, date, startDate, endDate);

      const [reportResponse, anomaliesResponse] = await Promise.allSettled([
        requestConfig.fetchReport(),
        activityAPI.getAnomalies({ page: 1, pageSize: 5000 }),
      ]);

      if (reportResponse.status !== 'fulfilled') {
        throw reportResponse.reason;
      }

      const anomalyItems = anomaliesResponse.status === 'fulfilled'
        ? (anomaliesResponse.value?.items || [])
        : [];

      const normalized = normalizeActivityReport({
        rawReport: reportResponse.value,
        anomalies: anomalyItems,
        startDate: requestConfig.startDate,
        endDate: requestConfig.endDate,
        groupBy: requestConfig.groupBy,
      });

      setReportData(normalized);
      setLastUpdated(new Date());
    } catch (err) {
      const message = err?.response?.data?.message || err?.message || 'Failed to load report data';
      setError(message);
      console.error('Analytics error:', err);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchReportData();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [reportType, date, startDate, endDate]);

  useEffect(() => {
    localStorage.setItem(ANALYTICS_LIVE_KEY, String(autoRefresh));
  }, [autoRefresh]);

  useEffect(() => {
    localStorage.setItem(ANALYTICS_INTERVAL_KEY, String(refreshIntervalSec));
  }, [refreshIntervalSec]);

  useEffect(() => {
    if (!autoRefresh) return undefined;

    const intervalId = window.setInterval(() => {
      if (document.hidden) return;
      fetchReportData();
    }, refreshIntervalSec * 1000);

    return () => window.clearInterval(intervalId);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [autoRefresh, refreshIntervalSec]);

  const persistPresets = (nextPresets) => {
    setPresets(nextPresets);
    localStorage.setItem(ANALYTICS_PRESETS_KEY, JSON.stringify(nextPresets));
  };

  const saveCurrentPreset = () => {
    const name = window.prompt('Название пресета', `Analytics ${reportType}`);
    if (!name) return;

    const nextPreset = {
      id: `${Date.now()}`,
      name: name.trim(),
      filters: { reportType, date, startDate, endDate },
    };
    persistPresets([nextPreset, ...presets].slice(0, 10));
    setSelectedPresetId(nextPreset.id);
  };

  const applyPreset = (presetId) => {
    setSelectedPresetId(presetId);
    const preset = presets.find((item) => item.id === presetId);
    if (!preset?.filters) return;

    setReportType(preset.filters.reportType || 'daily');
    setDate(preset.filters.date || formatDateInput(new Date()));
    setStartDate(preset.filters.startDate || formatDateInput(new Date(Date.now() - 7 * 24 * 60 * 60 * 1000)));
    setEndDate(preset.filters.endDate || formatDateInput(new Date()));
  };

  const deleteSelectedPreset = () => {
    if (!selectedPresetId) return;
    const nextPresets = presets.filter((preset) => preset.id !== selectedPresetId);
    persistPresets(nextPresets);
    setSelectedPresetId('');
  };

  const chartData = reportData?.timeline || [];
  const activityTypes = reportData?.activityTypes || [];
  const anomalyTypes = reportData?.anomalyTypes || [];
  const topComputers = reportData?.topComputers || [];
  const topProcesses = reportData?.topProcesses || [];
  const topUrls = reportData?.topUrls || [];
  const reportActivities = reportData?.activities || [];

  const drilldownRows = useMemo(() => {
    return drilldown.rows.map((row) => ({
      id: row.id,
      timestamp: row.timestamp,
      activityType: row.activityType,
      computerId: row.computerId,
      processName: row.processName || '-',
      url: row.url || '-',
      riskScore: Number(row.riskScore || 0),
      isBlocked: Boolean(row.isBlocked),
    }));
  }, [drilldown.rows]);

  const openDrilldown = (title, subtitle, rows) => {
    setDrilldown({
      open: true,
      title,
      subtitle,
      rows: rows.slice(0, 200),
    });
  };

  const handleTimelineClick = (chartState) => {
    const bucket = chartState?.activePayload?.[0]?.payload;
    if (!bucket) return;
    const rows = filterActivitiesByTimelineBucket(reportActivities, bucket.date, reportData?.range?.groupBy || 'day');
    openDrilldown('Timeline Drill-down', `Bucket: ${bucket.date}`, rows);
  };

  const handleActivityTypeClick = (entry) => {
    if (!entry?.name) return;
    const rows = reportActivities.filter((item) => String(item.activityType || '').toUpperCase() === String(entry.name).toUpperCase());
    openDrilldown('Activity Type Drill-down', `Type: ${entry.name}`, rows);
  };

  const handleTopComputerClick = (chartState) => {
    const row = chartState?.activePayload?.[0]?.payload;
    if (!row) return;
    const rows = reportActivities.filter((item) => String(item.computerId) === String(row.computerId));
    openDrilldown('Computer Drill-down', `Computer: ${row.computerName || row.computerId}`, rows);
  };

  const handleAnomalyTypeClick = (chartState) => {
    const row = chartState?.activePayload?.[0]?.payload;
    if (!row) return;
    const anomalyType = String(row.type || '');
    const anomalies = reportData?.anomalies || [];
    const rows = reportActivities.filter((item) => {
      return anomalies.some((anomaly) => anomaly.activityId === item.id && anomaly.type === anomalyType);
    });
    openDrilldown('Anomaly Type Drill-down', `Anomaly: ${anomalyType}`, rows);
  };

  if (loading && !reportData) {
    return (
      <Box display="flex" justifyContent="center" alignItems="center" minHeight="60vh">
        <CircularProgress />
      </Box>
    );
  }

  return (
    <Box className="mui-page-shell">
      <Box display="flex" justifyContent="space-between" alignItems="flex-start" mb={3} gap={2} flexWrap="wrap">
        <Box>
          <Typography variant="h4" gutterBottom>
            Analytics
          </Typography>
          <Typography variant="body1" color="text.secondary">
            Behavior and activity breakdowns from live service data
          </Typography>
        </Box>
        <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
          <FormControl size="small" sx={{ minWidth: 120 }}>
            <InputLabel>Interval</InputLabel>
            <Select
              label="Interval"
              value={refreshIntervalSec}
              onChange={(e) => setRefreshIntervalSec(Number(e.target.value))}
              disabled={!autoRefresh}
            >
              {[5, 10, 15, 30, 60].map((seconds) => (
                <MenuItem key={seconds} value={seconds}>{seconds}s</MenuItem>
              ))}
            </Select>
          </FormControl>
          <FormControlLabel
            control={<Switch checked={autoRefresh} onChange={(e) => setAutoRefresh(e.target.checked)} />}
            label="Live"
          />
          <Button variant="outlined" onClick={fetchReportData} disabled={loading}>
            {loading ? 'Refreshing...' : 'Refresh'}
          </Button>
        </Stack>
      </Box>

      {error && (
        <Alert severity="error" sx={{ mb: 3 }}>
          {error}
        </Alert>
      )}

      <Grid container spacing={2} mb={3}>
        <Grid item xs={12} md={3} sx={{ minWidth: 0 }}>
          <FormControl fullWidth>
            <InputLabel>Report Type</InputLabel>
            <Select
              value={reportType}
              label="Report Type"
              onChange={(e) => setReportType(e.target.value)}
            >
              <MenuItem value="daily">Daily</MenuItem>
              <MenuItem value="weekly">Weekly</MenuItem>
              <MenuItem value="monthly">Monthly</MenuItem>
              <MenuItem value="custom">Custom Range</MenuItem>
            </Select>
          </FormControl>
        </Grid>

        {reportType === 'daily' && (
          <Grid item xs={12} md={3} sx={{ minWidth: 0 }}>
            <TextField
              fullWidth
              type="date"
              label="Date"
              value={date}
              onChange={(e) => setDate(e.target.value)}
              InputLabelProps={{ shrink: true }}
            />
          </Grid>
        )}

        {reportType === 'weekly' && (
          <Grid item xs={12} md={3} sx={{ minWidth: 0 }}>
            <TextField
              fullWidth
              type="date"
              label="Week Start"
              value={startDate}
              onChange={(e) => setStartDate(e.target.value)}
              InputLabelProps={{ shrink: true }}
              helperText="Будет загружено 7 дней от выбранной даты"
            />
          </Grid>
        )}

        {reportType === 'custom' && (
          <>
            <Grid item xs={12} md={3} sx={{ minWidth: 0 }}>
              <TextField
                fullWidth
                type="date"
                label="Start Date"
                value={startDate}
                onChange={(e) => setStartDate(e.target.value)}
                InputLabelProps={{ shrink: true }}
              />
            </Grid>
            <Grid item xs={12} md={3} sx={{ minWidth: 0 }}>
              <TextField
                fullWidth
                type="date"
                label="End Date"
                value={endDate}
                onChange={(e) => setEndDate(e.target.value)}
                InputLabelProps={{ shrink: true }}
              />
            </Grid>
          </>
        )}

        <Grid item xs={12} md={3} sx={{ minWidth: 0 }}>
          <Card>
            <CardContent sx={{ py: 1.5 }}>
              <Typography variant="caption" color="text.secondary">
                Period
              </Typography>
              <Typography variant="body2" fontWeight={600}>
                {reportData?.range?.startDate || '-'}{reportData?.range?.endDate && reportData?.range?.endDate !== reportData?.range?.startDate ? ` -> ${reportData?.range?.endDate}` : ''}
              </Typography>
              <Typography variant="caption" color="text.secondary">
                Updated {lastUpdated ? lastUpdated.toLocaleTimeString() : '-'}
              </Typography>
            </CardContent>
          </Card>
        </Grid>

        <Grid item xs={12} md={6} sx={{ minWidth: 0 }}>
          <Card>
            <CardContent sx={{ py: 1.5 }}>
              <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1} alignItems={{ xs: 'stretch', sm: 'center' }}>
                <FormControl fullWidth size="small">
                  <InputLabel>Preset</InputLabel>
                  <Select
                    value={selectedPresetId}
                    label="Preset"
                    onChange={(e) => applyPreset(e.target.value)}
                  >
                    <MenuItem value="">No preset</MenuItem>
                    {presets.map((preset) => (
                      <MenuItem key={preset.id} value={preset.id}>{preset.name}</MenuItem>
                    ))}
                  </Select>
                </FormControl>
                <Button size="small" variant="outlined" onClick={saveCurrentPreset}>Save Preset</Button>
                <Button size="small" color="error" onClick={deleteSelectedPreset} disabled={!selectedPresetId}>
                  Delete
                </Button>
              </Stack>
            </CardContent>
          </Card>
        </Grid>
      </Grid>

      <Grid container spacing={2} mb={3}>
        <Grid item xs={12} sm={6} lg={3} sx={{ minWidth: 0 }}>
          <Card><CardContent>
            <Typography variant="body2" color="text.secondary">Total Activities</Typography>
            <Typography variant="h4">{reportData?.summary?.totalActivities || 0}</Typography>
          </CardContent></Card>
        </Grid>
        <Grid item xs={12} sm={6} lg={3} sx={{ minWidth: 0 }}>
          <Card><CardContent>
            <Typography variant="body2" color="text.secondary">Total Anomalies</Typography>
            <Typography variant="h4">{reportData?.summary?.totalAnomalies || 0}</Typography>
          </CardContent></Card>
        </Grid>
        <Grid item xs={12} sm={6} lg={3} sx={{ minWidth: 0 }}>
          <Card><CardContent>
            <Typography variant="body2" color="text.secondary">Blocked Activities</Typography>
            <Typography variant="h4">{reportData?.summary?.blockedActivities || 0}</Typography>
          </CardContent></Card>
        </Grid>
        <Grid item xs={12} sm={6} lg={3} sx={{ minWidth: 0 }}>
          <Card><CardContent>
            <Typography variant="body2" color="text.secondary">Average Risk Score</Typography>
            <Typography variant="h4">{(reportData?.summary?.averageRiskScore || 0).toFixed(1)}</Typography>
          </CardContent></Card>
        </Grid>
      </Grid>

      <Grid container spacing={3}>
        <Grid item xs={12} lg={8} sx={{ minWidth: 0 }}>
          <Paper sx={{ p: 2 }}>
            <Typography variant="h6" gutterBottom>Activity Timeline</Typography>
            <Box sx={{ width: '100%', height: { xs: 260, md: 320 }, minWidth: 0 }}>
              <ResponsiveContainer width="100%" height="100%" debounce={100}>
                <LineChart data={chartData} margin={{ top: 8, right: 16, left: 0, bottom: 0 }} onClick={handleTimelineClick}>
                  <CartesianGrid strokeDasharray="3 3" />
                  <XAxis dataKey="date" tick={{ fontSize: 12 }} minTickGap={20} />
                  <YAxis tick={{ fontSize: 12 }} />
                  <Tooltip />
                  <Legend />
                  <Line type="monotone" dataKey="count" stroke="#0f766e" strokeWidth={2.5} name="Activities" dot={false} />
                  <Line type="monotone" dataKey="anomalies" stroke="#b91c1c" strokeWidth={2} name="Anomalies" dot={false} />
                  <Line type="monotone" dataKey="riskScore" stroke="#0369a1" strokeWidth={2} name="Avg Risk" dot={false} />
                </LineChart>
              </ResponsiveContainer>
            </Box>
          </Paper>
        </Grid>

        <Grid item xs={12} lg={4} sx={{ minWidth: 0 }}>
          <Paper sx={{ p: 2 }}>
            <Typography variant="h6" gutterBottom>Activity Types</Typography>
            <Box sx={{ width: '100%', height: { xs: 260, md: 320 }, minWidth: 0 }}>
              <ResponsiveContainer width="100%" height="100%" debounce={100}>
                <PieChart>
                  <Pie
                    data={activityTypes}
                    cx="50%"
                    cy="50%"
                    outerRadius="72%"
                    dataKey="count"
                    nameKey="name"
                    labelLine={false}
                    onClick={handleActivityTypeClick}
                  >
                    {activityTypes.map((entry, index) => (
                      <Cell key={`activity-type-${entry.name}`} fill={COLORS[index % COLORS.length]} />
                    ))}
                  </Pie>
                  <Tooltip />
                  <Legend />
                </PieChart>
              </ResponsiveContainer>
            </Box>
          </Paper>
        </Grid>

        <Grid item xs={12} md={6} sx={{ minWidth: 0 }}>
          <Paper sx={{ p: 2 }}>
            <Typography variant="h6" gutterBottom>Anomaly Types</Typography>
            <Box sx={{ width: '100%', height: { xs: 240, md: 300 }, minWidth: 0 }}>
              <ResponsiveContainer width="100%" height="100%" debounce={100}>
                <BarChart data={anomalyTypes} margin={{ top: 8, right: 8, left: 0, bottom: 0 }} onClick={handleAnomalyTypeClick}>
                  <CartesianGrid strokeDasharray="3 3" />
                  <XAxis dataKey="type" tick={{ fontSize: 12 }} minTickGap={12} />
                  <YAxis tick={{ fontSize: 12 }} allowDecimals={false} />
                  <Tooltip />
                  <Bar dataKey="count" fill="#b91c1c" radius={[6, 6, 0, 0]} />
                </BarChart>
              </ResponsiveContainer>
            </Box>
          </Paper>
        </Grid>

        <Grid item xs={12} md={6} sx={{ minWidth: 0 }}>
          <Paper sx={{ p: 2 }}>
            <Typography variant="h6" gutterBottom>Top Computers by Activity</Typography>
            <Box sx={{ width: '100%', height: { xs: 240, md: 300 }, minWidth: 0 }}>
              <ResponsiveContainer width="100%" height="100%" debounce={100}>
                <BarChart data={topComputers} layout="vertical" margin={{ top: 8, right: 16, left: 20, bottom: 0 }} onClick={handleTopComputerClick}>
                  <CartesianGrid strokeDasharray="3 3" />
                  <XAxis type="number" tick={{ fontSize: 12 }} allowDecimals={false} />
                  <YAxis type="category" dataKey="computerName" width={70} tick={{ fontSize: 12 }} />
                  <Tooltip />
                  <Bar dataKey="count" fill="#0369a1" radius={[0, 6, 6, 0]} />
                </BarChart>
              </ResponsiveContainer>
            </Box>
          </Paper>
        </Grid>

        <Grid item xs={12} md={6} sx={{ minWidth: 0 }}>
          <Paper sx={{ p: 2 }}>
            <Stack direction="row" justifyContent="space-between" alignItems="center" mb={1.5}>
              <Typography variant="h6">Top Processes</Typography>
              <Chip size="small" label={`${topProcesses.length} items`} />
            </Stack>
            {topProcesses.length === 0 ? (
              <Typography variant="body2" color="text.secondary">No process names in selected range</Typography>
            ) : (
              <TableContainer sx={{ maxHeight: 280 }}>
                <Table stickyHeader size="small">
                  <TableHead>
                    <TableRow>
                      <TableCell>Process</TableCell>
                      <TableCell align="right">Count</TableCell>
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {topProcesses.map((row) => (
                      <TableRow key={row.processName} hover>
                        <TableCell>{row.processName}</TableCell>
                        <TableCell align="right">{row.count}</TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </TableContainer>
            )}
          </Paper>
        </Grid>

        <Grid item xs={12} md={6} sx={{ minWidth: 0 }}>
          <Paper sx={{ p: 2 }}>
            <Stack direction="row" justifyContent="space-between" alignItems="center" mb={1.5}>
              <Typography variant="h6">Top URLs</Typography>
              <Chip size="small" label={`${topUrls.length} items`} />
            </Stack>
            {topUrls.length === 0 ? (
              <Typography variant="body2" color="text.secondary">No URLs in selected range</Typography>
            ) : (
              <TableContainer sx={{ maxHeight: 280 }}>
                <Table stickyHeader size="small">
                  <TableHead>
                    <TableRow>
                      <TableCell>URL</TableCell>
                      <TableCell align="right">Count</TableCell>
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {topUrls.map((row) => (
                      <TableRow key={row.url} hover>
                        <TableCell sx={{ maxWidth: 360 }}>
                          <Typography noWrap title={row.url}>{row.url}</Typography>
                        </TableCell>
                        <TableCell align="right">{row.count}</TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </TableContainer>
            )}
          </Paper>
        </Grid>
      </Grid>

      <Dialog open={drilldown.open} onClose={() => setDrilldown((prev) => ({ ...prev, open: false }))} maxWidth="lg" fullWidth>
        <DialogTitle>{drilldown.title}</DialogTitle>
        <DialogContent>
          {drilldown.subtitle && (
            <Typography variant="body2" color="text.secondary" sx={{ mb: 1.5 }}>
              {drilldown.subtitle}
            </Typography>
          )}
          <TableContainer sx={{ maxHeight: 520 }}>
            <Table stickyHeader size="small">
              <TableHead>
                <TableRow>
                  <TableCell>ID</TableCell>
                  <TableCell>Time</TableCell>
                  <TableCell>Type</TableCell>
                  <TableCell align="right">Computer</TableCell>
                  <TableCell>Process</TableCell>
                  <TableCell>URL</TableCell>
                  <TableCell align="right">Risk</TableCell>
                  <TableCell>Status</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {drilldownRows.length === 0 ? (
                  <TableRow>
                    <TableCell colSpan={8} align="center">No activities matched the selected chart segment</TableCell>
                  </TableRow>
                ) : (
                  drilldownRows.map((row) => (
                    <TableRow key={row.id} hover>
                      <TableCell>{row.id}</TableCell>
                      <TableCell>{row.timestamp ? new Date(row.timestamp).toLocaleString() : '-'}</TableCell>
                      <TableCell>{row.activityType || '-'}</TableCell>
                      <TableCell align="right">{row.computerId ?? '-'}</TableCell>
                      <TableCell>{row.processName}</TableCell>
                      <TableCell sx={{ maxWidth: 260 }}>
                        <Typography noWrap title={row.url}>{row.url}</Typography>
                      </TableCell>
                      <TableCell align="right">{row.riskScore.toFixed(1)}</TableCell>
                      <TableCell>
                        <Chip size="small" color={row.isBlocked ? 'error' : 'success'} label={row.isBlocked ? 'Blocked' : 'Normal'} />
                      </TableCell>
                    </TableRow>
                  ))
                )}
              </TableBody>
            </Table>
          </TableContainer>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setDrilldown((prev) => ({ ...prev, open: false }))}>Close</Button>
        </DialogActions>
      </Dialog>
    </Box>
  );
};

export default Analytics;
