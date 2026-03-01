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
  IconButton,
  InputLabel,
  LinearProgress,
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
  OpenInFull,
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
import { activityAPI, normalizeStoredToken, reportsAPI, reportServiceAPI, userAPI } from '../services/api';
import {
  addDays,
  aggregateByDepartment,
  aggregateByUser,
  filterActivitiesByTimelineBucket,
  formatDateInput,
  getMonthBounds,
  normalizeActivityReport,
} from '../utils/reportTransforms';
import ChartExpandDialog from '../components/ChartExpandDialog';

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
  const [initialLoading, setInitialLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState(null);
  const [tabValue, setTabValue] = useState(0);
  const [reportType, setReportType] = useState('weekly');
  const [customStartDate, setCustomStartDate] = useState(formatDateInput(new Date(Date.now() - 6 * 24 * 60 * 60 * 1000)));
  const [customEndDate, setCustomEndDate] = useState(formatDateInput(new Date()));
  const [exportDialogOpen, setExportDialogOpen] = useState(false);
  const [exportFormat, setExportFormat] = useState('csv');
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
  const [expandedChart, setExpandedChart] = useState({
    open: false,
    chartKey: null,
    title: '',
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

  const loadReportsData = async ({ initial = false } = {}) => {
    try {
      if (initial) {
        setInitialLoading(true);
      } else {
        setRefreshing(true);
      }
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
      const message = err?.response?.data?.message || err?.message || 'Не удалось загрузить данные отчетов';
      setError(message);
      console.error('Ошибка загрузки отчетов:', err);
    } finally {
      setInitialLoading(false);
      setRefreshing(false);
    }
  };

  useEffect(() => {
    loadReportsData({ initial: true });
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
    const name = window.prompt('Название пресета', `Отчеты ${reportType}`);
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
          ? `Экспорт создан: ${result.fileName}`
          : 'Запрос на экспорт отправлен',
      });

      if (result?.downloadUrl) {
        const href = String(result.downloadUrl);
        const absoluteHref = href.startsWith('http://') || href.startsWith('https://')
          ? href
          : `${window.location.origin}${href.startsWith('/') ? '' : '/'}${href}`;

        const token = normalizeStoredToken(localStorage.getItem('token'));
        const downloadResp = await fetch(absoluteHref, {
          method: 'GET',
          headers: token ? { Authorization: `Bearer ${token}` } : {},
        });

        if (!downloadResp.ok) {
          throw new Error(`Не удалось скачать файл (HTTP ${downloadResp.status})`);
        }

        const blob = await downloadResp.blob();
        const blobUrl = URL.createObjectURL(blob);

        const a = document.createElement('a');
        a.href = blobUrl;
        a.download = result?.fileName || `report.${exportFormat}`;
        document.body.appendChild(a);
        a.click();
        a.remove();
        URL.revokeObjectURL(blobUrl);
      }

      setExportDialogOpen(false);
    } catch (err) {
      setExportMessage({
        severity: 'error',
        text: err?.response?.data?.message || err?.message || 'Не удалось экспортировать отчет',
      });
      console.error('Ошибка экспорта:', err);
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
    openDrilldown('Детализация временного сегмента', `Интервал: ${bucket.date}`, rows);
  };

  const handleDepartmentChartClick = (entry) => {
    const department = entry?.name;
    if (!department) return;
    const computerIds = new Set(
      (liveData.users || [])
        .filter((user) => (user?.department || 'Не назначен') === department && user?.computer?.id != null)
        .map((user) => user.computer.id)
    );
    const rows = reportActivities.filter((activity) => computerIds.has(activity?.computerId));
    openDrilldown('Детализация по отделу', `Отдел: ${department}`, rows);
  };

  const handleUserBarClick = (chartState) => {
    const row = chartState?.activePayload?.[0]?.payload;
    if (!row) return;
    const userRecord = (liveData.users || []).find((user) => user?.id === row.id);
    const computerId = userRecord?.computer?.id;
    const rows = computerId == null ? [] : reportActivities.filter((activity) => activity?.computerId === computerId);
    openDrilldown('Детализация по пользователю', `Пользователь: ${row.name}`, rows);
  };

  const openChartPreview = (chartKey, title) => {
    setExpandedChart({ open: true, chartKey, title });
  };

  const closeChartPreview = () => {
    setExpandedChart({ open: false, chartKey: null, title: '' });
  };

  const summaryCards = useMemo(() => {
    const reportSummary = liveData.reportSummary;
    const localSummary = normalizedReport?.summary;

    return [
      {
        id: 'activities',
        label: 'Всего активностей',
        value: reportSummary?.totalActivities ?? localSummary?.totalActivities ?? 0,
      },
      {
        id: 'anomalies',
        label: 'Аномалии',
        value: localSummary?.totalAnomalies ?? 0,
      },
      {
        id: 'users',
        label: 'Пользователи (сервис пользователей)',
        value: reportSummary?.totalUsers ?? liveData.users.length ?? 0,
      },
      {
        id: 'computers',
        label: 'Компьютеры',
        value: reportSummary?.totalComputers ?? normalizedReport?.topComputers?.length ?? 0,
      },
      {
        id: 'blocked',
        label: 'Заблокированные действия',
        value: reportSummary?.totalBlockedActions ?? localSummary?.blockedActivities ?? 0,
      },
      {
        id: 'risk',
        label: 'Средний риск',
        value: Number((reportSummary?.avgRiskScore ?? localSummary?.averageRiskScore ?? 0)).toFixed(1),
      },
    ];
  }, [liveData.reportSummary, normalizedReport, liveData.users.length]);

  const renderExpandedChart = () => {
    switch (expandedChart.chartKey) {
      case 'overviewTimeline':
        return (
          <ResponsiveContainer width="100%" height="100%" debounce={100}>
            <LineChart data={activityData} margin={{ top: 8, right: 16, left: 0, bottom: 0 }} onClick={handleTimelineClick}>
              <CartesianGrid strokeDasharray="3 3" />
              <XAxis dataKey="date" tick={{ fontSize: 12 }} minTickGap={20} />
              <YAxis tick={{ fontSize: 12 }} allowDecimals={false} />
              <Tooltip />
              <Legend />
              <Line type="monotone" dataKey="count" stroke="#0f766e" strokeWidth={2.5} name="Активности" dot={false} />
              <Line type="monotone" dataKey="anomalies" stroke="#b91c1c" strokeWidth={2} name="Аномалии" dot={false} />
            </LineChart>
          </ResponsiveContainer>
        );
      case 'departmentPie':
        return (
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
                  <Cell key={`expanded-dept-${entry.name}`} fill={entry.color} />
                ))}
              </Pie>
              <Tooltip />
              <Legend />
            </RePieChart>
          </ResponsiveContainer>
        );
      case 'trendTimeline':
        return (
          <ResponsiveContainer width="100%" height="100%" debounce={100}>
            <LineChart data={activityData} margin={{ top: 8, right: 16, left: 0, bottom: 0 }} onClick={handleTimelineClick}>
              <CartesianGrid strokeDasharray="3 3" />
              <XAxis dataKey="date" tick={{ fontSize: 12 }} minTickGap={20} />
              <YAxis yAxisId="left" tick={{ fontSize: 12 }} allowDecimals={false} />
              <YAxis yAxisId="right" orientation="right" tick={{ fontSize: 12 }} />
              <Tooltip />
              <Legend />
              <Line yAxisId="left" type="monotone" dataKey="count" stroke="#0f766e" strokeWidth={2.5} name="Активности" dot={false} />
              <Line yAxisId="left" type="monotone" dataKey="blocked" stroke="#d97706" strokeWidth={2} name="Заблокировано" dot={false} />
              <Line yAxisId="right" type="monotone" dataKey="riskScore" stroke="#0369a1" strokeWidth={2} name="Средний риск" dot={false} />
            </LineChart>
          </ResponsiveContainer>
        );
      case 'usersBar':
        return (
          <ResponsiveContainer width="100%" height="100%" debounce={100}>
            <ReBarChart data={liveData.userRows} layout="vertical" margin={{ top: 8, right: 16, left: 28, bottom: 0 }} onClick={handleUserBarClick}>
              <CartesianGrid strokeDasharray="3 3" />
              <XAxis type="number" allowDecimals={false} tick={{ fontSize: 12 }} />
              <YAxis type="category" dataKey="name" width={120} tick={{ fontSize: 12 }} />
              <Tooltip />
              <Legend />
              <Bar dataKey="activities" name="Активности" fill="#0f766e" radius={[0, 6, 6, 0]} />
              <Bar dataKey="blocked" name="Заблокировано" fill="#d97706" radius={[0, 6, 6, 0]} />
            </ReBarChart>
          </ResponsiveContainer>
        );
      default:
        return null;
    }
  };

  if (initialLoading && !normalizedReport) {
    return (
      <Box display="flex" justifyContent="center" alignItems="center" minHeight="60vh">
        <CircularProgress />
      </Box>
    );
  }

  return (
    <Box
      className="mui-page-shell"
      sx={{
        transition: 'opacity 160ms ease, transform 160ms ease',
        opacity: refreshing ? 0.95 : 1,
        transform: refreshing ? 'translateY(1px)' : 'translateY(0)',
      }}
    >
      <Box display="flex" justifyContent="space-between" alignItems="flex-start" gap={2} mb={3} flexWrap="wrap">
        <Box>
          <Typography variant="h4">Отчеты и аналитика</Typography>
          <Typography variant="body2" color="text.secondary">
            Данные в реальном времени из сервисов активности, пользователей и отчетов через шлюз
          </Typography>
          <Typography variant="caption" color="text.secondary">
            Период: {liveData.period?.label || '-'} | Обновлено: {lastUpdated ? lastUpdated.toLocaleTimeString('ru-RU') : '-'}
          </Typography>
        </Box>
        <Box display="flex" gap={1} flexWrap="wrap">
          <FormControl size="small" sx={{ minWidth: 112 }}>
            <InputLabel>Интервал</InputLabel>
            <Select
              label="Интервал"
              value={refreshIntervalSec}
              onChange={(e) => setRefreshIntervalSec(Number(e.target.value))}
              disabled={!autoRefresh}
            >
              {[10, 15, 30, 60].map((seconds) => (
                <MenuItem key={seconds} value={seconds}>{seconds} сек</MenuItem>
              ))}
            </Select>
          </FormControl>
          <FormControlLabel
            sx={{ ml: 0 }}
            control={<Switch checked={autoRefresh} onChange={(e) => setAutoRefresh(e.target.checked)} />}
            label="Онлайн"
          />
          <Button variant="outlined" onClick={() => loadReportsData()} disabled={refreshing}>
            {refreshing ? 'Обновление...' : 'Обновить'}
          </Button>
          <Button variant="contained" startIcon={<Download />} onClick={() => setExportDialogOpen(true)}>
            Экспорт отчета
          </Button>
        </Box>
      </Box>

      {refreshing && <LinearProgress sx={{ mb: 2, borderRadius: 999 }} />}

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
            <InputLabel>Период</InputLabel>
            <Select value={reportType} label="Период" onChange={(e) => setReportType(e.target.value)}>
              <MenuItem value="daily">День (сегодня)</MenuItem>
              <MenuItem value="weekly">Неделя (7 дней)</MenuItem>
              <MenuItem value="monthly">Месяц (текущий)</MenuItem>
              <MenuItem value="custom">Произвольный период</MenuItem>
            </Select>
          </FormControl>
        </Grid>

        {(reportType === 'weekly' || reportType === 'custom') && (
          <Grid item xs={12} md={3} sx={{ minWidth: 0 }}>
            <TextField
              fullWidth
              type="date"
              label={reportType === 'weekly' ? 'Начало недели' : 'Дата начала'}
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
              label="Дата окончания"
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
                  <InputLabel>Пресет</InputLabel>
                  <Select
                    value={selectedPresetId}
                    label="Пресет"
                    onChange={(e) => applyPreset(e.target.value)}
                  >
                    <MenuItem value="">Без пресета</MenuItem>
                    {presets.map((preset) => (
                      <MenuItem key={preset.id} value={preset.id}>{preset.name}</MenuItem>
                    ))}
                  </Select>
                </FormControl>
                <Button size="small" variant="outlined" onClick={saveCurrentPreset}>Сохранить</Button>
                <Button size="small" color="error" onClick={deleteSelectedPreset} disabled={!selectedPresetId}>
                  Удалить
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
        <Tab label="Обзор" icon={<Assessment />} iconPosition="start" />
        <Tab label="Тренды активности" icon={<Timeline />} iconPosition="start" />
        <Tab label="Анализ отделов" icon={<PieChart />} iconPosition="start" />
        <Tab label="Статистика пользователей" icon={<BarChart />} iconPosition="start" />
        <Tab label="Сформированные отчеты" icon={<FileDownload />} iconPosition="start" />
      </Tabs>

      {tabValue === 0 && (
        <Grid container spacing={3}>
          <Grid item xs={12} xl={7} sx={{ minWidth: 0 }}>
            <Card sx={{ height: '100%' }}>
              <CardContent sx={{ minWidth: 0 }}>
                <Box display="flex" justifyContent="space-between" alignItems="center" mb={1}>
                  <Typography variant="h6">Обзор активности (онлайн)</Typography>
                  <IconButton size="small" onClick={() => openChartPreview('overviewTimeline', 'Обзор активности')}>
                    <OpenInFull fontSize="small" />
                  </IconButton>
                </Box>
                <ChartBox>
                  <ResponsiveContainer width="100%" height="100%" debounce={100}>
                    <LineChart data={activityData} margin={{ top: 8, right: 16, left: 0, bottom: 0 }} onClick={handleTimelineClick}>
                      <CartesianGrid strokeDasharray="3 3" />
                      <XAxis dataKey="date" tick={{ fontSize: 12 }} minTickGap={20} />
                      <YAxis tick={{ fontSize: 12 }} allowDecimals={false} />
                      <Tooltip />
                      <Legend />
                      <Line type="monotone" dataKey="count" stroke="#0f766e" strokeWidth={2.5} name="Активности" dot={false} />
                      <Line type="monotone" dataKey="anomalies" stroke="#b91c1c" strokeWidth={2} name="Аномалии" dot={false} />
                    </LineChart>
                  </ResponsiveContainer>
                </ChartBox>
              </CardContent>
            </Card>
          </Grid>

          <Grid item xs={12} xl={5} sx={{ minWidth: 0 }}>
            <Card sx={{ height: '100%' }}>
              <CardContent sx={{ minWidth: 0 }}>
                <Box display="flex" justifyContent="space-between" alignItems="center" mb={1}>
                  <Typography variant="h6">Распределение по отделам (онлайн)</Typography>
                  <IconButton size="small" onClick={() => openChartPreview('departmentPie', 'Распределение по отделам')}>
                    <OpenInFull fontSize="small" />
                  </IconButton>
                </Box>
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
            <Box display="flex" justifyContent="space-between" alignItems="center" mb={1}>
              <Typography variant="h6">Тренды активности</Typography>
              <IconButton size="small" onClick={() => openChartPreview('trendTimeline', 'Тренды активности')}>
                <OpenInFull fontSize="small" />
              </IconButton>
            </Box>
            <ChartBox height={{ xs: 300, md: 420 }}>
              <ResponsiveContainer width="100%" height="100%" debounce={100}>
                <LineChart data={activityData} margin={{ top: 8, right: 16, left: 0, bottom: 0 }} onClick={handleTimelineClick}>
                  <CartesianGrid strokeDasharray="3 3" />
                  <XAxis dataKey="date" tick={{ fontSize: 12 }} minTickGap={20} />
                  <YAxis yAxisId="left" tick={{ fontSize: 12 }} allowDecimals={false} />
                  <YAxis yAxisId="right" orientation="right" tick={{ fontSize: 12 }} />
                  <Tooltip />
                  <Legend />
                  <Line yAxisId="left" type="monotone" dataKey="count" stroke="#0f766e" strokeWidth={2.5} name="Активности" dot={false} />
                  <Line yAxisId="left" type="monotone" dataKey="blocked" stroke="#d97706" strokeWidth={2} name="Заблокировано" dot={false} />
                  <Line yAxisId="right" type="monotone" dataKey="riskScore" stroke="#0369a1" strokeWidth={2} name="Средний риск" dot={false} />
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
                <Box display="flex" justifyContent="space-between" alignItems="center" mb={1}>
                  <Typography variant="h6">Распределение активности по отделам</Typography>
                  <IconButton size="small" onClick={() => openChartPreview('departmentPie', 'Распределение активности по отделам')}>
                    <OpenInFull fontSize="small" />
                  </IconButton>
                </Box>
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
                <Typography variant="h6" gutterBottom>Статистика отделов</Typography>
                <TableContainer sx={{ maxHeight: 420 }}>
                  <Table stickyHeader size="small">
                    <TableHead>
                      <TableRow>
                        <TableCell>Отдел</TableCell>
                        <TableCell align="right">Активности</TableCell>
                        <TableCell align="right">Пользователи</TableCell>
                        <TableCell align="right">Аномалии</TableCell>
                      </TableRow>
                    </TableHead>
                    <TableBody>
                      {liveData.departmentRows.length === 0 && (
                        <TableRow>
                          <TableCell colSpan={4} align="center">Нет данных по отделам</TableCell>
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
                <Box display="flex" justifyContent="space-between" alignItems="center" mb={1}>
                  <Typography variant="h6">Статистика активности пользователей (онлайн)</Typography>
                  <IconButton size="small" onClick={() => openChartPreview('usersBar', 'Статистика активности пользователей')}>
                    <OpenInFull fontSize="small" />
                  </IconButton>
                </Box>
                <ChartBox height={{ xs: 300, md: 420 }}>
                  <ResponsiveContainer width="100%" height="100%" debounce={100}>
                    <ReBarChart data={liveData.userRows} layout="vertical" margin={{ top: 8, right: 16, left: 28, bottom: 0 }} onClick={handleUserBarClick}>
                      <CartesianGrid strokeDasharray="3 3" />
                      <XAxis type="number" allowDecimals={false} tick={{ fontSize: 12 }} />
                      <YAxis type="category" dataKey="name" width={120} tick={{ fontSize: 12 }} />
                      <Tooltip />
                      <Legend />
                      <Bar dataKey="activities" name="Активности" fill="#0f766e" radius={[0, 6, 6, 0]} />
                      <Bar dataKey="blocked" name="Заблокировано" fill="#d97706" radius={[0, 6, 6, 0]} />
                    </ReBarChart>
                  </ResponsiveContainer>
                </ChartBox>
              </CardContent>
            </Card>
          </Grid>

          <Grid item xs={12} lg={5} sx={{ minWidth: 0 }}>
            <Card>
              <CardContent sx={{ minWidth: 0 }}>
                <Typography variant="h6" gutterBottom>Топ пользователей</Typography>
                <TableContainer sx={{ maxHeight: 420 }}>
                  <Table stickyHeader size="small">
                    <TableHead>
                      <TableRow>
                        <TableCell>Пользователь</TableCell>
                        <TableCell>Отдел</TableCell>
                        <TableCell align="right">Активности</TableCell>
                        <TableCell align="right">Средний риск</TableCell>
                      </TableRow>
                    </TableHead>
                    <TableBody>
                      {liveData.userRows.length === 0 && (
                        <TableRow>
                          <TableCell colSpan={4} align="center">Нет данных по активности пользователей</TableCell>
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
              <Typography variant="h6">Сформированные отчеты</Typography>
              <Chip label={`${liveData.generatedReports.length} записей`} color="primary" variant="outlined" />
            </Box>
            <TableContainer sx={{ overflowX: 'auto' }}>
              <Table size="small">
                <TableHead>
                  <TableRow>
                    <TableCell>ID</TableCell>
                    <TableCell>Дата отчета</TableCell>
                    <TableCell>Создан</TableCell>
                    <TableCell align="right">ID пользователя</TableCell>
                    <TableCell align="right">ID компьютера</TableCell>
                    <TableCell align="right">Активности</TableCell>
                    <TableCell align="right">Блокировки</TableCell>
                    <TableCell align="right">Средний риск</TableCell>
                    <TableCell>Статус</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {liveData.generatedReports.length === 0 && (
                    <TableRow>
                      <TableCell colSpan={9} align="center">
                        За выбранный период отчеты не найдены.
                      </TableCell>
                    </TableRow>
                  )}
                  {liveData.generatedReports.map((report) => (
                    <TableRow key={report.id} hover>
                      <TableCell>{report.id}</TableCell>
                      <TableCell>{report.reportDate || '-'}</TableCell>
                      <TableCell>{report.createdAt ? new Date(report.createdAt).toLocaleString('ru-RU') : '-'}</TableCell>
                      <TableCell align="right">{report.userId}</TableCell>
                      <TableCell align="right">{report.computerId}</TableCell>
                      <TableCell align="right">{report.totalActivities}</TableCell>
                      <TableCell align="right">{report.blockedActions}</TableCell>
                      <TableCell align="right">{Number(report.avgRiskScore || 0).toFixed(1)}</TableCell>
                      <TableCell>
                        <Chip label="Готов" color={getStatusColor('ready')} size="small" />
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </TableContainer>
          </CardContent>
        </Card>
      )}

      <ChartExpandDialog
        open={expandedChart.open}
        onClose={closeChartPreview}
        title={expandedChart.title || 'Диаграмма'}
      >
        {renderExpandedChart()}
      </ChartExpandDialog>

      <Dialog open={exportDialogOpen} onClose={() => setExportDialogOpen(false)} maxWidth="sm" fullWidth>
        <DialogTitle>Экспорт отчета</DialogTitle>
        <DialogContent>
          <Grid container spacing={2} sx={{ mt: 0.5 }}>
            <Grid item xs={12}>
              <FormControl fullWidth>
                <InputLabel>Формат экспорта</InputLabel>
                <Select
                  value={exportFormat}
                  label="Формат экспорта"
                  onChange={(e) => setExportFormat(e.target.value)}
                >
                  <MenuItem value="csv">CSV</MenuItem>
                  <MenuItem value="json">JSON</MenuItem>
                </Select>
              </FormControl>
            </Grid>
            <Grid item xs={12}>
              <Typography variant="body2" color="text.secondary">
                Выбранный период: {liveData.period?.label || '-'}
              </Typography>
              <Typography variant="body2" color="text.secondary">
                Источник: генератор экспорта шлюза (`/api/report/export`) с защищенной загрузкой файла
              </Typography>
            </Grid>
          </Grid>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setExportDialogOpen(false)} disabled={exporting}>Отмена</Button>
          <Button onClick={handleConfirmExport} variant="contained" disabled={exporting}>
            {exporting ? 'Экспорт...' : 'Экспорт'}
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
                  <TableCell>Время</TableCell>
                  <TableCell>Тип</TableCell>
                  <TableCell align="right">Компьютер</TableCell>
                  <TableCell align="right">Риск</TableCell>
                  <TableCell>Статус</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {drilldown.rows.length === 0 ? (
                  <TableRow>
                    <TableCell colSpan={6} align="center">Для выбранного сегмента активности не найдены</TableCell>
                  </TableRow>
                ) : (
                  drilldown.rows.map((row) => (
                    <TableRow key={row.id} hover>
                      <TableCell>{row.id}</TableCell>
                      <TableCell>{row.timestamp ? new Date(row.timestamp).toLocaleString('ru-RU') : '-'}</TableCell>
                      <TableCell>{row.activityType || '-'}</TableCell>
                      <TableCell align="right">{row.computerId ?? '-'}</TableCell>
                      <TableCell align="right">{Number(row.riskScore || 0).toFixed(1)}</TableCell>
                      <TableCell>
                        <Chip size="small" color={row.isBlocked ? 'error' : 'success'} label={row.isBlocked ? 'Заблокировано' : 'Норма'} />
                      </TableCell>
                    </TableRow>
                  ))
                )}
              </TableBody>
            </Table>
          </TableContainer>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setDrilldown((prev) => ({ ...prev, open: false }))}>Закрыть</Button>
        </DialogActions>
      </Dialog>
    </Box>
  );
};

export default Reports;
