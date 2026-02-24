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
  Select,
  Switch,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Tab,
  Tabs,
  TextField,
  Typography,
} from '@mui/material';
import {
  Assessment,
  Download,
  FileDownload,
  PieChart,
  Timeline,
  BarChart,
} from '@mui/icons-material';
import {
  Bar,
  BarChart as ReBarChart,
  CartesianGrid,
  Cell,
  Legend,
  Line,
  LineChart,
  Pie,
  PieChart as RePieChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts';
import { activityAPI, reportsAPI, reportServiceAPI, userAPI } from '../services/api';
import {
  addDays,
  aggregateByDepartment,
  aggregateByUser,
  filterActivitiesByTimelineBucket,
  formatDateInput,
  getMonthBounds,
  normalizeActivityReport,
} from '../utils/reportTransforms';

const DEPARTMENT_COLORS = ['#0f766e', '#0369a1', '#d97706', '#b91c1c', '#7c3aed', '#15803d', '#475569'];
const REPORTS_PRESETS_KEY = 'reports_presets_v1';
const REPORTS_LIVE_KEY = 'reports_auto_refresh';
const REPORTS_INTERVAL_KEY = 'reports_refresh_interval_sec';

const chartBoxSx = {
  width: '100%',
  minWidth: 0,
  height: { xs: 260, sm: 300, md: 340 },
};

const ChartBox = ({ children, height = chartBoxSx.height }) => (
  <Box sx={{ width: '100%', minWidth: 0, height }}>
    {children}
  </Box>
);

const getStatusColor = (value) => {
  if (!value) return 'default';
  const normalized = String(value).toLowerCase();
  if (normalized.includes('complete') || normalized.includes('ready') || normalized.includes('success')) return 'success';
  if (normalized.includes('process') || normalized.includes('pending')) return 'warning';
  if (normalized.includes('error') || normalized.includes('fail')) return 'error';
  return 'default';
};

const resolvePeriod = (reportType, customStart, customEnd) => {
  const today = formatDateInput(new Date());

  if (reportType === 'daily') {
    return { startDate: today, endDate: today, label: today };
  }

  if (reportType === 'weekly') {
    const startDate = customStart;
    const endDate = addDays(startDate, 6);
    return { startDate, endDate, label: `${startDate} -> ${endDate}` };
  }

  if (reportType === 'monthly') {
    const { startDate, endDate } = getMonthBounds(new Date());
    return { startDate, endDate, label: `${startDate} -> ${endDate}` };
  }

  return {
    startDate: customStart,
    endDate: customEnd,
    label: `${customStart} -> ${customEnd}`,
  };
};

const readReportPresets = () => {
  try {
    const raw = localStorage.getItem(REPORTS_PRESETS_KEY);
    const parsed = raw ? JSON.parse(raw) : [];
    return Array.isArray(parsed) ? parsed : [];
  } catch {
    return [];
  }
};

const Reports = () => {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [tabValue, setTabValue] = useState(0);
  const [reportType, setReportType] = useState('weekly');
  const [customStartDate, setCustomStartDate] = useState(formatDateInput(new Date(Date.now() - 6 * 24 * 60 * 60 * 1000)));
  const [customEndDate, setCustomEndDate] = useState(formatDateInput(new Date()));
  const [exportDialogOpen, setExportDialogOpen] = useState(false);
  const [exportFormat, setExportFormat] = useState('pdf');
  const [exporting, setExporting] = useState(false);
  const [exportMessage, setExportMessage] = useState(null);
  const [lastUpdated, setLastUpdated] = useState(null);
  const [autoRefresh, setAutoRefresh] = useState(() => {
    const raw = localStorage.getItem(REPORTS_LIVE_KEY);
    return raw == null ? false : raw === 'true';
  });
  const [refreshIntervalSec, setRefreshIntervalSec] = useState(() => {
    const parsed = Number(localStorage.getItem(REPORTS_INTERVAL_KEY));
    return Number.isFinite(parsed) && parsed >= 10 ? parsed : 20;
  });
  const [presets, setPresets] = useState(readReportPresets);
  const [selectedPresetId, setSelectedPresetId] = useState('');
  const [drilldown, setDrilldown] = useState({
    open: false,
    title: '',
    subtitle: '',
    rows: [],
  });
  const [liveData, setLiveData] = useState({
    period: { startDate: null, endDate: null, label: null },
    normalizedReport: null,
    users: [],
    anomalies: [],
    departmentRows: [],
    userRows: [],
    generatedReports: [],
    reportSummary: null,
  });

  const loadReportsData = async () => {
    try {
      setLoading(true);
      setError(null);
      setExportMessage(null);

      const period = resolvePeriod(reportType, customStartDate, customEndDate);

      const [customReportResult, anomaliesResult, usersResult, generatedReportsResult, reportSummaryResult] = await Promise.allSettled([
        reportsAPI.getCustomReport(period.startDate, period.endDate, { groupBy: 'day' }),
        activityAPI.getAnomalies({ page: 1, pageSize: 5000 }),
        userAPI.getUsers({ page: 1, pageSize: 500 }),
        reportServiceAPI.getDailyReportsRange(period.startDate, period.endDate, 1, 100),
        reportServiceAPI.getSummary(period.startDate, period.endDate),
      ]);

      if (customReportResult.status !== 'fulfilled') {
        throw customReportResult.reason;
      }

      const anomalies = anomaliesResult.status === 'fulfilled' ? (anomaliesResult.value?.items || []) : [];
      const users = usersResult.status === 'fulfilled' ? (usersResult.value?.users || []) : [];
      const generatedReports = generatedReportsResult.status === 'fulfilled'
        ? (generatedReportsResult.value?.reports || [])
        : [];
      const reportSummary = reportSummaryResult.status === 'fulfilled' ? reportSummaryResult.value : null;

      const normalizedReport = normalizeActivityReport({
        rawReport: customReportResult.value,
        anomalies,
        startDate: period.startDate,
        endDate: period.endDate,
        groupBy: 'day',
      });

      const departmentRows = aggregateByDepartment({
        users,
        activities: normalizedReport.activities,
        anomalies,
      });

      const userRows = aggregateByUser({
        users,
        activities: normalizedReport.activities,
      });

      setLiveData({
        period,
        normalizedReport,
        users,
        anomalies,
        departmentRows,
        userRows,
        generatedReports,
        reportSummary,
      });
      setLastUpdated(new Date());
    } catch (err) {
      const message = err?.response?.data?.message || err?.message || 'Failed to load report data';
      setError(message);
      console.error('Reports fetch error:', err);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadReportsData();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [reportType, customStartDate, customEndDate]);

  useEffect(() => {
    localStorage.setItem(REPORTS_LIVE_KEY, String(autoRefresh));
  }, [autoRefresh]);

  useEffect(() => {
    localStorage.setItem(REPORTS_INTERVAL_KEY, String(refreshIntervalSec));
  }, [refreshIntervalSec]);

  useEffect(() => {
    if (!autoRefresh) return undefined;
    const id = window.setInterval(() => {
      if (document.hidden) return;
      loadReportsData();
    }, refreshIntervalSec * 1000);
    return () => window.clearInterval(id);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [autoRefresh, refreshIntervalSec]);

  const persistPresets = (nextPresets) => {
    setPresets(nextPresets);
    localStorage.setItem(REPORTS_PRESETS_KEY, JSON.stringify(nextPresets));
  };

  const saveCurrentPreset = () => {
    const name = window.prompt('Название пресета', `Reports ${reportType}`);
    if (!name) return;
    const preset = {
      id: `${Date.now()}`,
      name: name.trim(),
      filters: { reportType, customStartDate, customEndDate },
    };
    persistPresets([preset, ...presets].slice(0, 10));
    setSelectedPresetId(preset.id);
  };

  const applyPreset = (presetId) => {
    setSelectedPresetId(presetId);
    const preset = presets.find((item) => item.id === presetId);
    if (!preset?.filters) return;
    setReportType(preset.filters.reportType || 'weekly');
    setCustomStartDate(preset.filters.customStartDate || formatDateInput(new Date(Date.now() - 6 * 24 * 60 * 60 * 1000)));
    setCustomEndDate(preset.filters.customEndDate || formatDateInput(new Date()));
  };

  const deleteSelectedPreset = () => {
    if (!selectedPresetId) return;
    persistPresets(presets.filter((p) => p.id !== selectedPresetId));
    setSelectedPresetId('');
  };

  const handleTabChange = (_event, newValue) => {
    setTabValue(newValue);
  };

  const handleConfirmExport = async () => {
    try {
      setExporting(true);
      setExportMessage(null);

      const period = liveData.period?.startDate && liveData.period?.endDate
        ? liveData.period
        : resolvePeriod(reportType, customStartDate, customEndDate);

      const result = await reportServiceAPI.exportReport({
        reportType,
        format: exportFormat,
        startDate: period.startDate,
        endDate: period.endDate,
        userId: 0,
        computerId: 0,
      });

      setExportMessage({
        severity: 'success',
        text: result?.fileName
          ? `Export created: ${result.fileName}`
          : 'Export request sent successfully',
      });
      setExportDialogOpen(false);
    } catch (err) {
      setExportMessage({
        severity: 'error',
        text: err?.response?.data?.message || err?.message || 'Failed to export report',
      });
      console.error('Export error:', err);
    } finally {
      setExporting(false);
    }
  };

  const normalizedReport = liveData.normalizedReport;
  const activityData = normalizedReport?.timeline || [];
  const reportActivities = normalizedReport?.activities || [];

  const departmentChartData = useMemo(() => {
    return liveData.departmentRows.map((row, index) => ({
      name: row.department,
      value: row.activities,
      color: DEPARTMENT_COLORS[index % DEPARTMENT_COLORS.length],
      users: row.users,
      anomalies: row.anomalies,
    }));
  }, [liveData.departmentRows]);

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
    const rows = filterActivitiesByTimelineBucket(reportActivities, bucket.date, normalizedReport?.range?.groupBy || 'day');
    openDrilldown('Timeline Drill-down', `Bucket: ${bucket.date}`, rows);
  };

  const handleDepartmentChartClick = (entry) => {
    const department = entry?.name;
    if (!department) return;
    const computerIds = new Set(
      (liveData.users || [])
        .filter((user) => (user?.department || 'Unassigned') === department && user?.computer?.id != null)
        .map((user) => user.computer.id)
    );
    const rows = reportActivities.filter((activity) => computerIds.has(activity?.computerId));
    openDrilldown('Department Drill-down', `Department: ${department}`, rows);
  };

  const handleUserBarClick = (chartState) => {
    const row = chartState?.activePayload?.[0]?.payload;
    if (!row) return;
    const userRecord = (liveData.users || []).find((user) => user?.id === row.id);
    const computerId = userRecord?.computer?.id;
    const rows = computerId == null ? [] : reportActivities.filter((activity) => activity?.computerId === computerId);
    openDrilldown('User Drill-down', `User: ${row.name}`, rows);
  };

  const summaryCards = useMemo(() => {
    const reportSummary = liveData.reportSummary;
    const localSummary = normalizedReport?.summary;

    return [
      {
        id: 'activities',
        label: 'Total Activities',
        value: reportSummary?.totalActivities ?? localSummary?.totalActivities ?? 0,
      },
      {
        id: 'anomalies',
        label: 'Anomalies',
        value: localSummary?.totalAnomalies ?? 0,
      },
      {
        id: 'users',
        label: 'Users (UserService)',
        value: reportSummary?.totalUsers ?? liveData.users.length ?? 0,
      },
      {
        id: 'computers',
        label: 'Computers',
        value: reportSummary?.totalComputers ?? normalizedReport?.topComputers?.length ?? 0,
      },
      {
        id: 'blocked',
        label: 'Blocked Actions',
        value: reportSummary?.totalBlockedActions ?? localSummary?.blockedActivities ?? 0,
      },
      {
        id: 'risk',
        label: 'Avg Risk Score',
        value: Number((reportSummary?.avgRiskScore ?? localSummary?.averageRiskScore ?? 0)).toFixed(1),
      },
    ];
  }, [liveData.reportSummary, normalizedReport, liveData.users.length]);

  if (loading && !normalizedReport) {
    return (
      <Box display="flex" justifyContent="center" alignItems="center" minHeight="60vh">
        <CircularProgress />
      </Box>
    );
  }

  return (
    <Box className="mui-page-shell">
      <Box display="flex" justifyContent="space-between" alignItems="flex-start" gap={2} mb={3} flexWrap="wrap">
        <Box>
          <Typography variant="h4">Reports & Analytics</Typography>
          <Typography variant="body2" color="text.secondary">
            Live data from ActivityService, UserService and ReportService via gateway
          </Typography>
          <Typography variant="caption" color="text.secondary">
            Period: {liveData.period?.label || '-'} | Updated: {lastUpdated ? lastUpdated.toLocaleTimeString() : '-'}
          </Typography>
        </Box>
        <Box display="flex" gap={1} flexWrap="wrap">
          <FormControl size="small" sx={{ minWidth: 112 }}>
            <InputLabel>Interval</InputLabel>
            <Select
              label="Interval"
              value={refreshIntervalSec}
              onChange={(e) => setRefreshIntervalSec(Number(e.target.value))}
              disabled={!autoRefresh}
            >
              {[10, 15, 30, 60].map((seconds) => (
                <MenuItem key={seconds} value={seconds}>{seconds}s</MenuItem>
              ))}
            </Select>
          </FormControl>
          <FormControlLabel
            sx={{ ml: 0 }}
            control={<Switch checked={autoRefresh} onChange={(e) => setAutoRefresh(e.target.checked)} />}
            label="Live"
          />
          <Button variant="outlined" onClick={loadReportsData} disabled={loading}>
            {loading ? 'Refreshing...' : 'Refresh'}
          </Button>
          <Button variant="contained" startIcon={<Download />} onClick={() => setExportDialogOpen(true)}>
            Export Report
          </Button>
        </Box>
      </Box>

      {error && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {error}
        </Alert>
      )}

      {exportMessage && (
        <Alert severity={exportMessage.severity} sx={{ mb: 2 }}>
          {exportMessage.text}
        </Alert>
      )}

      <Grid container spacing={2} mb={3}>
        <Grid item xs={12} md={3} sx={{ minWidth: 0 }}>
          <FormControl fullWidth>
            <InputLabel>Period</InputLabel>
            <Select value={reportType} label="Period" onChange={(e) => setReportType(e.target.value)}>
              <MenuItem value="daily">Daily (today)</MenuItem>
              <MenuItem value="weekly">Weekly (7 days)</MenuItem>
              <MenuItem value="monthly">Monthly (current month)</MenuItem>
              <MenuItem value="custom">Custom range</MenuItem>
            </Select>
          </FormControl>
        </Grid>

        {(reportType === 'weekly' || reportType === 'custom') && (
          <Grid item xs={12} md={3} sx={{ minWidth: 0 }}>
            <TextField
              fullWidth
              type="date"
              label={reportType === 'weekly' ? 'Week Start' : 'Start Date'}
              value={customStartDate}
              onChange={(e) => setCustomStartDate(e.target.value)}
              InputLabelProps={{ shrink: true }}
            />
          </Grid>
        )}

        {reportType === 'custom' && (
          <Grid item xs={12} md={3} sx={{ minWidth: 0 }}>
            <TextField
              fullWidth
              type="date"
              label="End Date"
              value={customEndDate}
              onChange={(e) => setCustomEndDate(e.target.value)}
              InputLabelProps={{ shrink: true }}
            />
          </Grid>
        )}

        <Grid item xs={12} md={6} sx={{ minWidth: 0 }}>
          <Card>
            <CardContent sx={{ py: 1.5 }}>
              <Box display="flex" gap={1} flexWrap="wrap" alignItems="center">
                <FormControl size="small" sx={{ minWidth: 220, flex: '1 1 220px' }}>
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
                <Button size="small" variant="outlined" onClick={saveCurrentPreset}>Save</Button>
                <Button size="small" color="error" onClick={deleteSelectedPreset} disabled={!selectedPresetId}>
                  Delete
                </Button>
              </Box>
            </CardContent>
          </Card>
        </Grid>
      </Grid>

      <Grid container spacing={2} mb={3}>
        {summaryCards.map((item) => (
          <Grid item xs={12} sm={6} lg={2} key={item.id} sx={{ minWidth: 0 }}>
            <Card sx={{ height: '100%' }}>
              <CardContent>
                <Typography variant="body2" color="text.secondary">{item.label}</Typography>
                <Typography variant="h5">{item.value}</Typography>
              </CardContent>
            </Card>
          </Grid>
        ))}
      </Grid>

      <Tabs
        value={tabValue}
        onChange={handleTabChange}
        sx={{ mb: 3 }}
        variant="scrollable"
        allowScrollButtonsMobile
      >
        <Tab label="Overview" icon={<Assessment />} iconPosition="start" />
        <Tab label="Activity Trends" icon={<Timeline />} iconPosition="start" />
        <Tab label="Department Analysis" icon={<PieChart />} iconPosition="start" />
        <Tab label="User Statistics" icon={<BarChart />} iconPosition="start" />
        <Tab label="Generated Reports" icon={<FileDownload />} iconPosition="start" />
      </Tabs>

      {tabValue === 0 && (
        <Grid container spacing={3}>
          <Grid item xs={12} xl={7} sx={{ minWidth: 0 }}>
            <Card sx={{ height: '100%' }}>
              <CardContent sx={{ minWidth: 0 }}>
                <Typography variant="h6" gutterBottom>Activity Overview (live)</Typography>
                <ChartBox>
                  <ResponsiveContainer width="100%" height="100%" debounce={100}>
                    <LineChart data={activityData} margin={{ top: 8, right: 16, left: 0, bottom: 0 }} onClick={handleTimelineClick}>
                      <CartesianGrid strokeDasharray="3 3" />
                      <XAxis dataKey="date" tick={{ fontSize: 12 }} minTickGap={20} />
                      <YAxis tick={{ fontSize: 12 }} allowDecimals={false} />
                      <Tooltip />
                      <Legend />
                      <Line type="monotone" dataKey="count" stroke="#0f766e" strokeWidth={2.5} name="Activities" dot={false} />
                      <Line type="monotone" dataKey="anomalies" stroke="#b91c1c" strokeWidth={2} name="Anomalies" dot={false} />
                    </LineChart>
                  </ResponsiveContainer>
                </ChartBox>
              </CardContent>
            </Card>
          </Grid>

          <Grid item xs={12} xl={5} sx={{ minWidth: 0 }}>
            <Card sx={{ height: '100%' }}>
              <CardContent sx={{ minWidth: 0 }}>
                <Typography variant="h6" gutterBottom>Department Distribution (live)</Typography>
                <ChartBox>
                  <ResponsiveContainer width="100%" height="100%" debounce={100}>
                    <RePieChart>
                      <Pie
                        data={departmentChartData}
                        cx="50%"
                        cy="50%"
                        outerRadius="72%"
                        dataKey="value"
                        nameKey="name"
                        labelLine={false}
                        onClick={handleDepartmentChartClick}
                      >
                        {departmentChartData.map((entry) => (
                          <Cell key={`dept-${entry.name}`} fill={entry.color} />
                        ))}
                      </Pie>
                      <Tooltip />
                      <Legend />
                    </RePieChart>
                  </ResponsiveContainer>
                </ChartBox>
              </CardContent>
            </Card>
          </Grid>
        </Grid>
      )}

      {tabValue === 1 && (
        <Card>
          <CardContent sx={{ minWidth: 0 }}>
            <Typography variant="h6" gutterBottom>Activity Trends (ActivityService)</Typography>
            <ChartBox height={{ xs: 300, md: 420 }}>
              <ResponsiveContainer width="100%" height="100%" debounce={100}>
                <LineChart data={activityData} margin={{ top: 8, right: 16, left: 0, bottom: 0 }} onClick={handleTimelineClick}>
                  <CartesianGrid strokeDasharray="3 3" />
                  <XAxis dataKey="date" tick={{ fontSize: 12 }} minTickGap={20} />
                  <YAxis yAxisId="left" tick={{ fontSize: 12 }} allowDecimals={false} />
                  <YAxis yAxisId="right" orientation="right" tick={{ fontSize: 12 }} />
                  <Tooltip />
                  <Legend />
                  <Line yAxisId="left" type="monotone" dataKey="count" stroke="#0f766e" strokeWidth={2.5} name="Activities" dot={false} />
                  <Line yAxisId="left" type="monotone" dataKey="blocked" stroke="#d97706" strokeWidth={2} name="Blocked" dot={false} />
                  <Line yAxisId="right" type="monotone" dataKey="riskScore" stroke="#0369a1" strokeWidth={2} name="Avg Risk Score" dot={false} />
                </LineChart>
              </ResponsiveContainer>
            </ChartBox>
          </CardContent>
        </Card>
      )}

      {tabValue === 2 && (
        <Grid container spacing={3}>
          <Grid item xs={12} lg={6} sx={{ minWidth: 0 }}>
            <Card sx={{ height: '100%' }}>
              <CardContent sx={{ minWidth: 0 }}>
                <Typography variant="h6" gutterBottom>Department Activity Distribution</Typography>
                <ChartBox>
                  <ResponsiveContainer width="100%" height="100%" debounce={100}>
                    <RePieChart>
                      <Pie
                        data={departmentChartData}
                        cx="50%"
                        cy="50%"
                        outerRadius="74%"
                        dataKey="value"
                        nameKey="name"
                        labelLine={false}
                        onClick={handleDepartmentChartClick}
                      >
                        {departmentChartData.map((entry) => (
                          <Cell key={`dept-pie-${entry.name}`} fill={entry.color} />
                        ))}
                      </Pie>
                      <Tooltip />
                      <Legend />
                    </RePieChart>
                  </ResponsiveContainer>
                </ChartBox>
              </CardContent>
            </Card>
          </Grid>

          <Grid item xs={12} lg={6} sx={{ minWidth: 0 }}>
            <Card sx={{ height: '100%' }}>
              <CardContent sx={{ minWidth: 0 }}>
                <Typography variant="h6" gutterBottom>Department Statistics</Typography>
                <TableContainer sx={{ maxHeight: 420 }}>
                  <Table stickyHeader size="small">
                    <TableHead>
                      <TableRow>
                        <TableCell>Department</TableCell>
                        <TableCell align="right">Activities</TableCell>
                        <TableCell align="right">Users</TableCell>
                        <TableCell align="right">Anomalies</TableCell>
                      </TableRow>
                    </TableHead>
                    <TableBody>
                      {liveData.departmentRows.length === 0 && (
                        <TableRow>
                          <TableCell colSpan={4} align="center">No department data</TableCell>
                        </TableRow>
                      )}
                      {liveData.departmentRows.map((dept, index) => (
                        <TableRow key={dept.department} hover>
                          <TableCell>
                            <Chip
                              label={dept.department}
                              size="small"
                              sx={{
                                backgroundColor: DEPARTMENT_COLORS[index % DEPARTMENT_COLORS.length],
                                color: '#fff',
                              }}
                            />
                          </TableCell>
                          <TableCell align="right">{dept.activities}</TableCell>
                          <TableCell align="right">{dept.users}</TableCell>
                          <TableCell align="right">{dept.anomalies}</TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </TableContainer>
              </CardContent>
            </Card>
          </Grid>
        </Grid>
      )}

      {tabValue === 3 && (
        <Grid container spacing={3}>
          <Grid item xs={12} lg={7} sx={{ minWidth: 0 }}>
            <Card>
              <CardContent sx={{ minWidth: 0 }}>
                <Typography variant="h6" gutterBottom>User Activity Statistics (live)</Typography>
                <ChartBox height={{ xs: 300, md: 420 }}>
                  <ResponsiveContainer width="100%" height="100%" debounce={100}>
                    <ReBarChart data={liveData.userRows} layout="vertical" margin={{ top: 8, right: 16, left: 28, bottom: 0 }} onClick={handleUserBarClick}>
                      <CartesianGrid strokeDasharray="3 3" />
                      <XAxis type="number" allowDecimals={false} tick={{ fontSize: 12 }} />
                      <YAxis type="category" dataKey="name" width={120} tick={{ fontSize: 12 }} />
                      <Tooltip />
                      <Legend />
                      <Bar dataKey="activities" fill="#0f766e" radius={[0, 6, 6, 0]} />
                      <Bar dataKey="blocked" fill="#d97706" radius={[0, 6, 6, 0]} />
                    </ReBarChart>
                  </ResponsiveContainer>
                </ChartBox>
              </CardContent>
            </Card>
          </Grid>

          <Grid item xs={12} lg={5} sx={{ minWidth: 0 }}>
            <Card>
              <CardContent sx={{ minWidth: 0 }}>
                <Typography variant="h6" gutterBottom>Top Users</Typography>
                <TableContainer sx={{ maxHeight: 420 }}>
                  <Table stickyHeader size="small">
                    <TableHead>
                      <TableRow>
                        <TableCell>User</TableCell>
                        <TableCell>Dept</TableCell>
                        <TableCell align="right">Activities</TableCell>
                        <TableCell align="right">Avg Risk</TableCell>
                      </TableRow>
                    </TableHead>
                    <TableBody>
                      {liveData.userRows.length === 0 && (
                        <TableRow>
                          <TableCell colSpan={4} align="center">No user activity data</TableCell>
                        </TableRow>
                      )}
                      {liveData.userRows.map((row) => (
                        <TableRow key={row.id} hover>
                          <TableCell>{row.name}</TableCell>
                          <TableCell>{row.department}</TableCell>
                          <TableCell align="right">{row.activities}</TableCell>
                          <TableCell align="right">{row.avgRiskScore.toFixed(1)}</TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </TableContainer>
              </CardContent>
            </Card>
          </Grid>
        </Grid>
      )}

      {tabValue === 4 && (
        <Card>
          <CardContent sx={{ minWidth: 0 }}>
            <Box display="flex" justifyContent="space-between" alignItems="center" mb={2} gap={2} flexWrap="wrap">
              <Typography variant="h6">Generated Reports (ReportService)</Typography>
              <Chip label={`${liveData.generatedReports.length} rows`} color="primary" variant="outlined" />
            </Box>
            <TableContainer sx={{ overflowX: 'auto' }}>
              <Table size="small">
                <TableHead>
                  <TableRow>
                    <TableCell>ID</TableCell>
                    <TableCell>Report Date</TableCell>
                    <TableCell>Created At</TableCell>
                    <TableCell align="right">User ID</TableCell>
                    <TableCell align="right">Computer ID</TableCell>
                    <TableCell align="right">Activities</TableCell>
                    <TableCell align="right">Blocked</TableCell>
                    <TableCell align="right">Avg Risk</TableCell>
                    <TableCell>Status</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {liveData.generatedReports.length === 0 && (
                    <TableRow>
                      <TableCell colSpan={9} align="center">
                        No generated reports found for selected period.
                      </TableCell>
                    </TableRow>
                  )}
                  {liveData.generatedReports.map((report) => (
                    <TableRow key={report.id} hover>
                      <TableCell>{report.id}</TableCell>
                      <TableCell>{report.reportDate || '-'}</TableCell>
                      <TableCell>{report.createdAt ? new Date(report.createdAt).toLocaleString() : '-'}</TableCell>
                      <TableCell align="right">{report.userId}</TableCell>
                      <TableCell align="right">{report.computerId}</TableCell>
                      <TableCell align="right">{report.totalActivities}</TableCell>
                      <TableCell align="right">{report.blockedActions}</TableCell>
                      <TableCell align="right">{Number(report.avgRiskScore || 0).toFixed(1)}</TableCell>
                      <TableCell>
                        <Chip label="Ready" color={getStatusColor('ready')} size="small" />
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </TableContainer>
          </CardContent>
        </Card>
      )}

      <Dialog open={exportDialogOpen} onClose={() => setExportDialogOpen(false)} maxWidth="sm" fullWidth>
        <DialogTitle>Export Report</DialogTitle>
        <DialogContent>
          <Grid container spacing={2} sx={{ mt: 0.5 }}>
            <Grid item xs={12}>
              <FormControl fullWidth>
                <InputLabel>Export Format</InputLabel>
                <Select
                  value={exportFormat}
                  label="Export Format"
                  onChange={(e) => setExportFormat(e.target.value)}
                >
                  <MenuItem value="pdf">PDF</MenuItem>
                  <MenuItem value="excel">Excel</MenuItem>
                  <MenuItem value="csv">CSV</MenuItem>
                </Select>
              </FormControl>
            </Grid>
            <Grid item xs={12}>
              <Typography variant="body2" color="text.secondary">
                Selected period: {liveData.period?.label || '-'}
              </Typography>
              <Typography variant="body2" color="text.secondary">
                Source: ReportService export endpoint (`/api/report/export`)
              </Typography>
            </Grid>
          </Grid>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setExportDialogOpen(false)} disabled={exporting}>Cancel</Button>
          <Button onClick={handleConfirmExport} variant="contained" disabled={exporting}>
            {exporting ? 'Exporting...' : 'Export'}
          </Button>
        </DialogActions>
      </Dialog>

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
                  <TableCell>Timestamp</TableCell>
                  <TableCell>Type</TableCell>
                  <TableCell align="right">Computer</TableCell>
                  <TableCell align="right">Risk</TableCell>
                  <TableCell>Status</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {drilldown.rows.length === 0 ? (
                  <TableRow>
                    <TableCell colSpan={6} align="center">No activities matched this segment</TableCell>
                  </TableRow>
                ) : (
                  drilldown.rows.map((row) => (
                    <TableRow key={row.id} hover>
                      <TableCell>{row.id}</TableCell>
                      <TableCell>{row.timestamp ? new Date(row.timestamp).toLocaleString() : '-'}</TableCell>
                      <TableCell>{row.activityType || '-'}</TableCell>
                      <TableCell align="right">{row.computerId ?? '-'}</TableCell>
                      <TableCell align="right">{Number(row.riskScore || 0).toFixed(1)}</TableCell>
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

export default Reports;
