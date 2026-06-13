import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { alpha, useTheme } from '@mui/material/styles';
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  Checkbox,
  Chip,
  CircularProgress,
  Divider,
  FormControl,
  Grid,
  InputAdornment,
  InputLabel,
  List,
  ListItemButton,
  ListItemIcon,
  ListItemText,
  MenuItem,
  Paper,
  Select,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  TextField,
  Tooltip,
  Typography,
} from '@mui/material';
import {
  Search,
  Refresh,
  Memory,
  Computer,
  Terminal,
  Lock,
  LockOpen,
  Send,
  CheckCircle,
  ErrorOutline,
  Schedule,
  Policy,
  Replay,
} from '@mui/icons-material';
import { agentAPI } from '../services/api';

const FETCH_PAGE_SIZE = 500;
const COMMAND_PAGE_SIZE = 20;

const formatDateTime = (value) => {
  if (!value) return '—';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return String(value);
  return date.toLocaleString('ru-RU');
};

const formatRelative = (value) => {
  if (!value) return '—';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '—';
  const diffMs = Date.now() - date.getTime();
  const diffSec = Math.floor(diffMs / 1000);
  if (diffSec < 60) return `${diffSec} сек назад`;
  const diffMin = Math.floor(diffSec / 60);
  if (diffMin < 60) return `${diffMin} мин назад`;
  const diffHour = Math.floor(diffMin / 60);
  if (diffHour < 24) return `${diffHour} ч назад`;
  return `${Math.floor(diffHour / 24)} дн назад`;
};

const getStatusColor = (status) => {
  const s = String(status || '').toLowerCase();
  if (s.includes('online') || s.includes('active')) return 'success';
  if (s.includes('offline')) return 'warning';
  if (s.includes('error') || s.includes('failed')) return 'error';
  return 'default';
};

const getCommandStatusIcon = (status) => {
  const s = String(status || '').toLowerCase();
  if (s === 'success') return <CheckCircle fontSize="small" color="success" />;
  if (s === 'failed') return <ErrorOutline fontSize="small" color="error" />;
  return <Schedule fontSize="small" color="action" />;
};

const canRetryCommand = (status) => {
  const normalized = String(status || '').toLowerCase();
  return normalized === 'deadletter' || normalized === 'timeout' || normalized === 'failed';
};

const toUtcIso = (value) => {
  if (!value) return '';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '';
  return date.toISOString();
};

const toBool = (value) => (value === true ? true : value === false ? false : null);

const buildEffectiveCapabilities = (policy) => {
  if (!policy) return [];

  return [
    {
      key: 'processes',
      label: 'Сбор процессов',
      enabled: toBool(policy.enableProcessCollection),
      detail: policy.processSnapshotLimit ? `Лимит ${policy.processSnapshotLimit}` : null,
    },
    {
      key: 'browser',
      label: 'История браузера',
      enabled: toBool(policy.enableBrowserCollection),
      detail: Array.isArray(policy.browsers) && policy.browsers.length > 0 ? policy.browsers.join(', ') : null,
    },
    {
      key: 'window',
      label: 'Отслеживание активного окна',
      enabled: toBool(policy.enableActiveWindowCollection),
      detail: null,
    },
    {
      key: 'idle',
      label: 'Отслеживание бездействия',
      enabled: toBool(policy.enableIdleCollection),
      detail: policy.idleThresholdSec ? `Порог ${policy.idleThresholdSec}с` : null,
    },
    {
      key: 'autolock',
      label: 'Автоблокировка при высоком риске',
      enabled: toBool(policy.autoLockEnabled),
      detail: policy.highRiskThreshold != null ? `Риск ≥ ${policy.highRiskThreshold}` : null,
    },
  ];
};

const extractReportedCapabilities = (agent) => {
  const raw = agent?.capabilities ?? agent?.reportedCapabilities ?? agent?.metadata?.capabilities ?? null;
  if (!raw) return [];

  if (Array.isArray(raw)) {
    return raw.map((item, index) => {
      if (typeof item === 'string') return { key: `${index}-${item}`, label: item, enabled: true, detail: null };
      if (item && typeof item === 'object') {
        const label = item.label || item.name || item.key || `Capability ${index + 1}`;
        return {
          key: String(item.key || label || index),
          label: String(label),
          enabled: item.enabled !== false,
          detail: item.detail ? String(item.detail) : null,
        };
      }
      return { key: String(index), label: String(item), enabled: true, detail: null };
    });
  }

  if (typeof raw === 'object') {
    return Object.entries(raw).map(([key, value]) => {
      if (typeof value === 'boolean') return { key, label: key, enabled: value, detail: null };
      return { key, label: key, enabled: true, detail: value == null ? null : String(value) };
    });
  }

  return [];
};

const parseJsonValue = (value, fallback = null) => {
  if (!value) return fallback;
  if (typeof value === 'object') return value;
  try {
    return JSON.parse(value);
  } catch {
    return fallback;
  }
};

const formatBytes = (value) => {
  const bytes = Number(value || 0);
  if (!Number.isFinite(bytes) || bytes <= 0) return '—';
  const units = ['Б', 'КБ', 'МБ', 'ГБ', 'ТБ'];
  let size = bytes;
  let index = 0;
  while (size >= 1024 && index < units.length - 1) {
    size /= 1024;
    index += 1;
  }
  return `${size.toFixed(index === 0 ? 0 : 1)} ${units[index]}`;
};

const normalizeAgent = (agent) => {
  const capabilities = parseJsonValue(agent.capabilitiesJson, agent.capabilities || {});
  const health = parseJsonValue(agent.healthJson, {});
  return {
    id: agent.id,
    computerId: agent.computerId,
    version: agent.version || '—',
    status: agent.status || 'unknown',
    lastHeartbeat: agent.lastHeartbeat,
    configVersion: agent.configVersion || '—',
    offlineSince: agent.offlineSince,
    queueSize: agent.queueSize,
    lastCollectedAt: agent.lastCollectedAt,
    lastSentAt: agent.lastSentAt,
    lastError: agent.lastError,
    policyVersion: agent.policyVersion,
    sourcePlatform: agent.sourcePlatform,
    capabilities,
    reportedCapabilities: agent.reportedCapabilities,
    health,
    systemInventory: health?.system_inventory || null,
    metadata: agent.metadata,
  };
};

const prettyJson = (value) => {
  if (!value) return '{}';
  if (typeof value === 'string') {
    try {
      return JSON.stringify(JSON.parse(value), null, 2);
    } catch {
      return value;
    }
  }
  try {
    return JSON.stringify(value, null, 2);
  } catch {
    return String(value);
  }
};

const Agents = () => {
  const theme = useTheme();
  const [loadingList, setLoadingList] = useState(true);
  const [loadingDetails, setLoadingDetails] = useState(false);
  const [actionLoading, setActionLoading] = useState(false);
  const [error, setError] = useState(null);
  const [success, setSuccess] = useState(null);

  const [agents, setAgents] = useState([]);
  const [selectedAgentId, setSelectedAgentId] = useState(null);
  const [selectedAgent, setSelectedAgent] = useState(null);
  const [policy, setPolicy] = useState(null);
  const [policyVersions, setPolicyVersions] = useState([]);
  const [policyVersionsTotal, setPolicyVersionsTotal] = useState(0);
  const [commands, setCommands] = useState([]);
  const [commandsTotal, setCommandsTotal] = useState(0);

  const [searchTerm, setSearchTerm] = useState('');
  const [statusFilter, setStatusFilter] = useState('all');
  const [commandStatusFilter, setCommandStatusFilter] = useState('all');
  const [commandTypeFilter, setCommandTypeFilter] = useState('all');
  const [commandFromFilter, setCommandFromFilter] = useState('');
  const [commandToFilter, setCommandToFilter] = useState('');
  const [commandPage, setCommandPage] = useState(1);
  const [policyVersionsPage, setPolicyVersionsPage] = useState(1);
  const [selectedAgentIds, setSelectedAgentIds] = useState([]);

  const [customCommandType, setCustomCommandType] = useState('PING');
  const [customCommandPayload, setCustomCommandPayload] = useState('{}');
  const [adminReason, setAdminReason] = useState('Заблокировано администратором');

  const buildCommandQuery = useCallback(({ page = commandPage, pageSize = COMMAND_PAGE_SIZE, status = commandStatusFilter } = {}) => {
    const query = {
      page,
      pageSize,
      ...(status !== 'all' ? { status } : {}),
      ...(commandTypeFilter !== 'all' ? { type: commandTypeFilter } : {}),
    };

    const createdFrom = toUtcIso(commandFromFilter);
    const createdTo = toUtcIso(commandToFilter);
    if (createdFrom) query.from = createdFrom;
    if (createdTo) query.to = createdTo;

    return query;
  }, [commandPage, commandStatusFilter, commandTypeFilter, commandFromFilter, commandToFilter]);

  useEffect(() => {
    let alive = true;

    const fetchAgents = async () => {
      try {
        setLoadingList(true);
        setError(null);
        const response = await agentAPI.getAgents({
          page: 1,
          pageSize: FETCH_PAGE_SIZE,
          ...(statusFilter !== 'all' ? { status: statusFilter } : {}),
        });

        if (!alive) return;
        const rows = (response?.agents || []).map(normalizeAgent);
        setAgents(rows);
        setSelectedAgentIds((prev) => {
          const allowed = new Set(rows.map((row) => row.id));
          return (Array.isArray(prev) ? prev : []).filter((id) => allowed.has(id));
        });

        setSelectedAgentId((prev) => {
          if (prev && rows.some((a) => a.id === prev)) return prev;
          return rows[0]?.id ?? null;
        });
      } catch (err) {
        if (!alive) return;
        setError(err?.response?.data?.message || err?.message || 'Не удалось загрузить агентов');
      } finally {
        if (alive) setLoadingList(false);
      }
    };

    fetchAgents();
    return () => { alive = false; };
  }, [statusFilter]);

  useEffect(() => {
    setCommandPage(1);
    setPolicyVersionsPage(1);
  }, [selectedAgentId, commandStatusFilter, commandTypeFilter, commandFromFilter, commandToFilter]);

  useEffect(() => {
    if (!selectedAgentId) {
      setSelectedAgent(null);
      setPolicy(null);
      setPolicyVersions([]);
      setPolicyVersionsTotal(0);
      setCommands([]);
      setCommandsTotal(0);
      return;
    }

    let alive = true;
    const fetchDetails = async () => {
      try {
        setLoadingDetails(true);
        setError(null);

        const [agentResp, policyResp, policyVersionsResp, commandsResp] = await Promise.all([
          agentAPI.getAgentById(selectedAgentId),
          agentAPI.getAgentPolicy(selectedAgentId),
          agentAPI.getAgentPolicyVersions(selectedAgentId, {
            page: policyVersionsPage,
            pageSize: 10,
          }),
          agentAPI.getAgentCommands(selectedAgentId, buildCommandQuery()),
        ]);

        if (!alive) return;

        setSelectedAgent(normalizeAgent(agentResp));
        setPolicy(policyResp || null);
        setPolicyVersions(policyVersionsResp?.versions || []);
        setPolicyVersionsTotal(policyVersionsResp?.totalCount || 0);
        setCommands(commandsResp?.commands || []);
        setCommandsTotal(commandsResp?.totalCount || 0);
      } catch (err) {
        if (!alive) return;
        setError(err?.response?.data?.message || err?.message || 'Не удалось загрузить данные агента');
      } finally {
        if (alive) setLoadingDetails(false);
      }
    };

    fetchDetails();
    return () => { alive = false; };
  }, [selectedAgentId, policyVersionsPage, buildCommandQuery]);

  const filteredAgents = useMemo(() => {
    const query = searchTerm.trim().toLowerCase();
    if (!query) return agents;

    return agents.filter((agent) => {
      const haystack = [
        agent.id,
        agent.computerId,
        agent.status,
        agent.version,
        agent.configVersion,
      ].join(' ').toLowerCase();
      return haystack.includes(query);
    });
  }, [agents, searchTerm]);

  const filteredAgentIds = useMemo(() => filteredAgents.map((agent) => agent.id), [filteredAgents]);
  const allFilteredSelected = filteredAgentIds.length > 0 && filteredAgentIds.every((id) => selectedAgentIds.includes(id));
  const selectedAgentCount = selectedAgentIds.length;

  const toggleAgentSelection = (agentId) => {
    setSelectedAgentIds((prev) => {
      const current = Array.isArray(prev) ? prev : [];
      if (current.includes(agentId)) return current.filter((id) => id !== agentId);
      return [...current, agentId];
    });
  };

  const toggleSelectAllFiltered = () => {
    setSelectedAgentIds((prev) => {
      const current = new Set(Array.isArray(prev) ? prev : []);
      if (allFilteredSelected) {
        filteredAgentIds.forEach((id) => current.delete(id));
      } else {
        filteredAgentIds.forEach((id) => current.add(id));
      }
      return Array.from(current);
    });
  };

  const effectiveCapabilities = useMemo(() => buildEffectiveCapabilities(policy), [policy]);
  const reportedCapabilities = useMemo(() => extractReportedCapabilities(selectedAgent), [selectedAgent]);
  const systemInventory = selectedAgent?.systemInventory || null;

  const clearSuccessLater = () => {
    setTimeout(() => setSuccess(null), 2500);
  };

  const hardRefresh = async () => {
    try {
      setActionLoading(true);
      setError(null);
      const listResp = await agentAPI.getAgents({
        page: 1,
        pageSize: FETCH_PAGE_SIZE,
        ...(statusFilter !== 'all' ? { status: statusFilter } : {}),
      });
      const rows = (listResp?.agents || []).map(normalizeAgent);
      setAgents(rows);
      setSelectedAgentIds((prev) => {
        const allowed = new Set(rows.map((row) => row.id));
        return (Array.isArray(prev) ? prev : []).filter((id) => allowed.has(id));
      });
      if (selectedAgentId && rows.some((a) => a.id === selectedAgentId)) {
        const [agentResp, policyResp, policyVersionsResp, commandsResp] = await Promise.all([
          agentAPI.getAgentById(selectedAgentId),
          agentAPI.getAgentPolicy(selectedAgentId),
          agentAPI.getAgentPolicyVersions(selectedAgentId, {
            page: policyVersionsPage,
            pageSize: 10,
          }),
          agentAPI.getAgentCommands(selectedAgentId, buildCommandQuery()),
        ]);
        setSelectedAgent(normalizeAgent(agentResp));
        setPolicy(policyResp || null);
        setPolicyVersions(policyVersionsResp?.versions || []);
        setPolicyVersionsTotal(policyVersionsResp?.totalCount || 0);
        setCommands(commandsResp?.commands || []);
        setCommandsTotal(commandsResp?.totalCount || 0);
      }
      setSuccess('Список агентов обновлен');
      clearSuccessLater();
    } catch (err) {
      setError(err?.response?.data?.message || err?.message || 'Не удалось обновить данные');
    } finally {
      setActionLoading(false);
    }
  };

  const handleBulkBlock = async (shouldBlock) => {
    if (selectedAgentCount === 0) return;

    const actionLabel = shouldBlock ? 'заблокировать' : 'разблокировать';
    const confirmed = window.confirm(`${actionLabel} ${selectedAgentCount} выбранных агентов?`);
    if (!confirmed) return;

    try {
      setActionLoading(true);
      setError(null);

      const response = await agentAPI.bulkSetWorkstationState({
        agentIds: selectedAgentIds,
        blocked: shouldBlock,
        reason: adminReason || (shouldBlock ? 'Заблокировано администратором' : 'Разблокировано администратором'),
      });

      const successCount = Number(response?.successCount) || 0;
      const failureCount = Number(response?.failureCount) || 0;
      setSuccess(
        `${shouldBlock ? 'Блокировка' : 'Разблокировка'}: успешно ${successCount}, с ошибками ${failureCount}`
      );
      clearSuccessLater();

      await hardRefresh();
    } catch (err) {
      setError(err?.response?.data?.message || err?.message || 'Не удалось выполнить массовую операцию');
    } finally {
      setActionLoading(false);
    }
  };

  const handleQuickBlock = async (shouldBlock) => {
    if (!selectedAgentId) return;
    try {
      setActionLoading(true);
      setError(null);
      if (shouldBlock) {
        await agentAPI.blockWorkstation(selectedAgentId, adminReason || 'Заблокировано администратором');
        setSuccess('Команда блокировки поставлена в очередь');
      } else {
        await agentAPI.unblockWorkstation(selectedAgentId, adminReason || 'Разблокировано администратором');
        setSuccess('Команда разблокировки поставлена в очередь');
      }
      clearSuccessLater();
      setCommandPage(1);
      // detail refetch via effect on commandPage won't trigger if already 1, fetch directly
      const [agentResp, policyResp, policyVersionsResp, commandsResp] = await Promise.all([
        agentAPI.getAgentById(selectedAgentId),
        agentAPI.getAgentPolicy(selectedAgentId),
        agentAPI.getAgentPolicyVersions(selectedAgentId, {
          page: policyVersionsPage,
          pageSize: 10,
        }),
        agentAPI.getAgentCommands(selectedAgentId, buildCommandQuery({ page: 1 })),
      ]);
      setSelectedAgent(normalizeAgent(agentResp));
      setPolicy(policyResp || null);
      setPolicyVersions(policyVersionsResp?.versions || []);
      setPolicyVersionsTotal(policyVersionsResp?.totalCount || 0);
      setCommands(commandsResp?.commands || []);
      setCommandsTotal(commandsResp?.totalCount || 0);
    } catch (err) {
      setError(err?.response?.data?.message || err?.message || 'Не удалось поставить команду в очередь');
    } finally {
      setActionLoading(false);
    }
  };

  const handleSendCustomCommand = async () => {
    if (!selectedAgentId) return;

    try {
      JSON.parse(customCommandPayload || '{}');
    } catch {
      setError('Некорректный JSON полезной нагрузки');
      return;
    }

    try {
      setActionLoading(true);
      setError(null);
      const command = await agentAPI.createAgentCommand(selectedAgentId, {
        type: String(customCommandType || '').trim().toUpperCase(),
        payloadJson: customCommandPayload || '{}',
      });
      setSuccess(`Команда поставлена в очередь: ${command?.type || customCommandType}`);
      clearSuccessLater();
      setCommandPage(1);
      const commandsResp = await agentAPI.getAgentCommands(selectedAgentId, buildCommandQuery({ page: 1 }));
      setCommands(commandsResp?.commands || []);
      setCommandsTotal(commandsResp?.totalCount || 0);
    } catch (err) {
      setError(err?.response?.data?.message || err?.message || 'Не удалось создать команду');
    } finally {
      setActionLoading(false);
    }
  };

  const handleRetryCommand = async (commandId) => {
    if (!selectedAgentId || !commandId) return;

    try {
      setActionLoading(true);
      setError(null);
      const response = await agentAPI.retryAgentCommand(selectedAgentId, commandId);

      setSuccess(response?.message || `Команда #${commandId} поставлена на повтор`);
      clearSuccessLater();

      const nextFilter =
        commandStatusFilter === 'all' || commandStatusFilter === 'pending'
          ? commandStatusFilter
          : 'all';

      if (nextFilter !== commandStatusFilter) {
        setCommandStatusFilter(nextFilter);
      }
      setCommandPage(1);

      const commandsResp = await agentAPI.getAgentCommands(
        selectedAgentId,
        buildCommandQuery({ page: 1, status: nextFilter })
      );
      setCommands(commandsResp?.commands || []);
      setCommandsTotal(commandsResp?.totalCount || 0);
    } catch (err) {
      setError(err?.response?.data?.message || err?.message || 'Не удалось повторить команду');
    } finally {
      setActionLoading(false);
    }
  };

  const handleDeleteAgent = async () => {
    if (!selectedAgentId) return;
    const confirmed = window.confirm(`Удалить агента #${selectedAgentId}? Действие необратимо.`);
    if (!confirmed) return;

    try {
      setActionLoading(true);
      setError(null);
      await agentAPI.deleteAgent(selectedAgentId);
      setSuccess(`Агент #${selectedAgentId} удален`);
      clearSuccessLater();
      const nextAgents = agents.filter((a) => a.id !== selectedAgentId);
      setAgents(nextAgents);
      setSelectedAgentIds((prev) => (Array.isArray(prev) ? prev.filter((id) => id !== selectedAgentId) : []));
      setSelectedAgentId(nextAgents[0]?.id ?? null);
    } catch (err) {
      setError(err?.response?.data?.message || err?.message || 'Не удалось удалить агента');
    } finally {
      setActionLoading(false);
    }
  };

  const handleRestorePolicyVersion = async (versionId) => {
    if (!selectedAgentId || !versionId) return;
    const confirmed = window.confirm(`Восстановить версию политики #${versionId} для агента #${selectedAgentId}?`);
    if (!confirmed) return;

    try {
      setActionLoading(true);
      setError(null);
      const result = await agentAPI.restoreAgentPolicyVersion(selectedAgentId, versionId, {});
      setPolicy(result?.policy || null);
      setSuccess(result?.message || `Версия политики #${versionId} восстановлена`);
      clearSuccessLater();

      const versionsResp = await agentAPI.getAgentPolicyVersions(selectedAgentId, {
        page: policyVersionsPage,
        pageSize: 10,
      });
      setPolicyVersions(versionsResp?.versions || []);
      setPolicyVersionsTotal(versionsResp?.totalCount || 0);
    } catch (err) {
      setError(err?.response?.data?.message || err?.message || 'Не удалось восстановить версию политики');
    } finally {
      setActionLoading(false);
    }
  };

  const commandPages = Math.max(1, Math.ceil(commandsTotal / COMMAND_PAGE_SIZE));
  const policyVersionPages = Math.max(1, Math.ceil(policyVersionsTotal / 10));

  return (
    <Box>
      <Box display="flex" justifyContent="space-between" alignItems="center" mb={3} gap={2} flexWrap="wrap">
        <Box>
          <Typography variant="h4">Агенты</Typography>
          <Typography variant="body2" color="text.secondary">
            Реестр агентов, эффективные возможности и история команд
          </Typography>
        </Box>
        <Stack direction="row" spacing={1} flexWrap="wrap">
          <Button
            variant="contained"
            startIcon={actionLoading ? <CircularProgress size={16} color="inherit" /> : <Refresh />}
            onClick={hardRefresh}
            disabled={actionLoading}
          >
            Обновить
          </Button>
        </Stack>
      </Box>

      {error && (
        <Alert severity="error" sx={{ mb: 2 }} onClose={() => setError(null)}>
          {error}
        </Alert>
      )}
      {success && (
        <Alert severity="success" sx={{ mb: 2 }} onClose={() => setSuccess(null)}>
          {success}
        </Alert>
      )}

      <Grid container spacing={3}>
        <Grid item xs={12} lg={4}>
          <Card sx={{ height: '100%' }}>
            <CardContent>
              <Stack spacing={2}>
                <TextField
                  fullWidth
                  size="small"
                  placeholder="Поиск агентов"
                  value={searchTerm}
                  onChange={(e) => setSearchTerm(e.target.value)}
                  InputProps={{
                    startAdornment: (
                      <InputAdornment position="start">
                        <Search fontSize="small" />
                      </InputAdornment>
                    ),
                  }}
                />

                <FormControl fullWidth size="small">
                  <InputLabel>Статус</InputLabel>
                  <Select
                    label="Статус"
                    value={statusFilter}
                    onChange={(e) => setStatusFilter(e.target.value)}
                  >
                    <MenuItem value="all">Все</MenuItem>
                    <MenuItem value="online">Онлайн</MenuItem>
                    <MenuItem value="offline">Оффлайн</MenuItem>
                    <MenuItem value="active">Активен</MenuItem>
                    <MenuItem value="error">Ошибка</MenuItem>
                  </Select>
                </FormControl>

                <Paper variant="outlined" sx={{ p: 1.25 }}>
                  <Stack spacing={1}>
                    <Stack direction="row" alignItems="center" justifyContent="space-between">
                      <Stack direction="row" spacing={1} alignItems="center">
                        <Checkbox
                          size="small"
                          checked={allFilteredSelected}
                          indeterminate={!allFilteredSelected && selectedAgentCount > 0}
                          onChange={toggleSelectAllFiltered}
                        />
                        <Typography variant="body2">Выбрано: {selectedAgentCount}</Typography>
                      </Stack>
                    </Stack>
                    <Stack direction="row" spacing={1}>
                      <Button
                        size="small"
                        variant="outlined"
                        color="error"
                        startIcon={<Lock />}
                        disabled={actionLoading || selectedAgentCount === 0}
                        onClick={() => handleBulkBlock(true)}
                      >
                        Блокировать
                      </Button>
                      <Button
                        size="small"
                        variant="outlined"
                        color="success"
                        startIcon={<LockOpen />}
                        disabled={actionLoading || selectedAgentCount === 0}
                        onClick={() => handleBulkBlock(false)}
                      >
                        Разблокировать
                      </Button>
                    </Stack>
                  </Stack>
                </Paper>

                <Divider />

                {loadingList ? (
                  <Box display="flex" justifyContent="center" py={4}><CircularProgress /></Box>
                ) : filteredAgents.length === 0 ? (
                  <Alert severity="info">Агенты не найдены</Alert>
                ) : (
                  <List disablePadding sx={{ maxHeight: 640, overflowY: 'auto' }}>
                    {filteredAgents.map((agent) => {
                      const selected = agent.id === selectedAgentId;
                      return (
                        <ListItemButton
                          key={agent.id}
                          selected={selected}
                          onClick={() => setSelectedAgentId(agent.id)}
                          sx={{
                            mb: 1,
                            borderRadius: 2,
                            alignItems: 'flex-start',
                            border: '1px solid',
                            borderColor: selected ? 'primary.main' : 'divider',
                            backgroundColor: selected ? alpha(theme.palette.primary.main, theme.palette.mode === 'dark' ? 0.14 : 0.06) : 'transparent',
                          }}
                        >
                          <Checkbox
                            size="small"
                            checked={selectedAgentIds.includes(agent.id)}
                            onClick={(event) => event.stopPropagation()}
                            onChange={() => toggleAgentSelection(agent.id)}
                            sx={{ mt: 0.3, mr: 0.5 }}
                          />
                          <ListItemIcon sx={{ minWidth: 38, mt: 0.3 }}>
                            <Computer color={selected ? 'primary' : 'action'} fontSize="small" />
                          </ListItemIcon>
                          <ListItemText
                            primary={
                              <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap">
                                <Typography variant="subtitle2">Агент #{agent.id}</Typography>
                                <Chip size="small" label={agent.status || 'неизвестно'} color={getStatusColor(agent.status)} />
                              </Stack>
                            }
                            secondary={
                              <Box mt={0.5}>
                                <Typography variant="caption" display="block" color="text.secondary">
                                  Компьютер #{agent.computerId} • v{agent.version}
                                </Typography>
                                <Typography variant="caption" display="block" color="text.secondary">
                                  Последний сигнал: {formatRelative(agent.lastHeartbeat)}
                                </Typography>
                              </Box>
                            }
                          />
                        </ListItemButton>
                      );
                    })}
                  </List>
                )}
              </Stack>
            </CardContent>
          </Card>
        </Grid>

        <Grid item xs={12} lg={8}>
          {!selectedAgentId ? (
            <Alert severity="info">Выберите агента для просмотра деталей.</Alert>
          ) : loadingDetails && !selectedAgent ? (
            <Box display="flex" justifyContent="center" py={8}><CircularProgress /></Box>
          ) : (
            <Stack spacing={3}>
              <Card>
                <CardContent>
                  <Box display="flex" justifyContent="space-between" alignItems="flex-start" gap={2} flexWrap="wrap">
                    <Box>
                      <Typography variant="h5">Агент #{selectedAgent?.id}</Typography>
                      <Typography variant="body2" color="text.secondary">
                        Компьютер #{selectedAgent?.computerId} • Версия {selectedAgent?.version || '—'} • Конфиг {selectedAgent?.configVersion || '—'}
                      </Typography>
                    </Box>
                    <Stack direction="row" spacing={1} flexWrap="wrap">
                      <Chip label={selectedAgent?.status || 'неизвестно'} color={getStatusColor(selectedAgent?.status)} />
                      {policy?.adminBlocked ? (
                        <Chip label="Заблокирован администратором" color="error" variant="filled" />
                      ) : (
                        <Chip label="Не заблокирован" color="success" variant="outlined" />
                      )}
                    </Stack>
                  </Box>

                  <Grid container spacing={2} sx={{ mt: 1 }}>
                    <Grid item xs={12} sm={6} md={3}>
                      <Paper variant="outlined" sx={{ p: 1.5 }}>
                        <Typography variant="caption" color="text.secondary">Последний сигнал</Typography>
                        <Typography variant="body2">{formatDateTime(selectedAgent?.lastHeartbeat)}</Typography>
                      </Paper>
                    </Grid>
                    <Grid item xs={12} sm={6} md={3}>
                      <Paper variant="outlined" sx={{ p: 1.5 }}>
                        <Typography variant="caption" color="text.secondary">Оффлайн с</Typography>
                        <Typography variant="body2">{formatDateTime(selectedAgent?.offlineSince)}</Typography>
                      </Paper>
                    </Grid>
                    <Grid item xs={12} sm={6} md={3}>
                      <Paper variant="outlined" sx={{ p: 1.5 }}>
                        <Typography variant="caption" color="text.secondary">Версия политики</Typography>
                        <Typography variant="body2">{policy?.policyVersion || '—'}</Typography>
                      </Paper>
                    </Grid>
                    <Grid item xs={12} sm={6} md={3}>
                      <Paper variant="outlined" sx={{ p: 1.5 }}>
                        <Typography variant="caption" color="text.secondary">Политика обновлена</Typography>
                        <Typography variant="body2">{formatDateTime(policy?.updatedAt)}</Typography>
                      </Paper>
                    </Grid>
                  </Grid>

                  {policy?.adminBlocked && policy?.blockedReason && (
                    <Alert severity="warning" sx={{ mt: 2 }}>{policy.blockedReason}</Alert>
                  )}
                </CardContent>
              </Card>

              {systemInventory && (
                <Card>
                  <CardContent>
                    <Stack direction="row" spacing={1} alignItems="center" mb={2}>
                      <Computer color="primary" fontSize="small" />
                      <Typography variant="h6">Информация о компьютере</Typography>
                    </Stack>
                    <Grid container spacing={2}>
                      <Grid item xs={12} sm={6} md={3}>
                        <Paper variant="outlined" sx={{ p: 1.5 }}>
                          <Typography variant="caption" color="text.secondary">Имя хоста</Typography>
                          <Typography variant="body2" sx={{ wordBreak: 'break-word' }}>{systemInventory.hostname || '—'}</Typography>
                        </Paper>
                      </Grid>
                      <Grid item xs={12} sm={6} md={3}>
                        <Paper variant="outlined" sx={{ p: 1.5 }}>
                          <Typography variant="caption" color="text.secondary">ОС</Typography>
                          <Typography variant="body2" sx={{ wordBreak: 'break-word' }}>{systemInventory.platform || selectedAgent?.sourcePlatform || '—'}</Typography>
                        </Paper>
                      </Grid>
                      <Grid item xs={12} sm={6} md={3}>
                        <Paper variant="outlined" sx={{ p: 1.5 }}>
                          <Typography variant="caption" color="text.secondary">Пользователь</Typography>
                          <Typography variant="body2" sx={{ wordBreak: 'break-word' }}>{systemInventory.current_user || '—'}</Typography>
                        </Paper>
                      </Grid>
                      <Grid item xs={12} sm={6} md={3}>
                        <Paper variant="outlined" sx={{ p: 1.5 }}>
                          <Typography variant="caption" color="text.secondary">Права процесса</Typography>
                          <Typography variant="body2">{systemInventory.is_admin ? 'Администратор' : 'Обычный пользователь'}</Typography>
                        </Paper>
                      </Grid>
                      <Grid item xs={12} sm={6} md={3}>
                        <Paper variant="outlined" sx={{ p: 1.5 }}>
                          <Typography variant="caption" color="text.secondary">CPU</Typography>
                          <Typography variant="body2">
                            {systemInventory.cpu?.physical_cores || '—'} / {systemInventory.cpu?.logical_cores || '—'} ядер
                          </Typography>
                        </Paper>
                      </Grid>
                      <Grid item xs={12} sm={6} md={3}>
                        <Paper variant="outlined" sx={{ p: 1.5 }}>
                          <Typography variant="caption" color="text.secondary">RAM</Typography>
                          <Typography variant="body2">{formatBytes(systemInventory.memory?.total_bytes)}</Typography>
                        </Paper>
                      </Grid>
                      <Grid item xs={12} sm={6} md={3}>
                        <Paper variant="outlined" sx={{ p: 1.5 }}>
                          <Typography variant="caption" color="text.secondary">Диски</Typography>
                          <Typography variant="body2">{Array.isArray(systemInventory.disks) ? systemInventory.disks.length : 0}</Typography>
                        </Paper>
                      </Grid>
                      <Grid item xs={12} sm={6} md={3}>
                        <Paper variant="outlined" sx={{ p: 1.5 }}>
                          <Typography variant="caption" color="text.secondary">Сетевые интерфейсы</Typography>
                          <Typography variant="body2">
                            {Array.isArray(systemInventory.network_interfaces) ? systemInventory.network_interfaces.length : 0}
                          </Typography>
                        </Paper>
                      </Grid>
                    </Grid>
                  </CardContent>
                </Card>
              )}

              <Grid container spacing={3}>
                <Grid item xs={12} md={6}>
                  <Card sx={{ height: '100%' }}>
                    <CardContent>
                      <Stack direction="row" spacing={1} alignItems="center" mb={2}>
                        <Policy color="primary" fontSize="small" />
                        <Typography variant="h6">Эффективные возможности (политика)</Typography>
                      </Stack>
                      <Stack spacing={1.25}>
                        {effectiveCapabilities.length === 0 ? (
                          <Alert severity="info">Политика еще не загружена.</Alert>
                        ) : effectiveCapabilities.map((cap) => (
                          <Paper key={cap.key} variant="outlined" sx={{ p: 1.25 }}>
                            <Stack direction="row" justifyContent="space-between" alignItems="center" gap={1}>
                              <Box>
                                <Typography variant="body2" sx={{ fontWeight: 600 }}>{cap.label}</Typography>
                                {cap.detail && (
                                  <Typography variant="caption" color="text.secondary">{cap.detail}</Typography>
                                )}
                              </Box>
                              <Chip
                                size="small"
                                color={cap.enabled ? 'success' : 'default'}
                                label={cap.enabled ? 'Включено' : 'Отключено'}
                                variant={cap.enabled ? 'filled' : 'outlined'}
                              />
                            </Stack>
                          </Paper>
                        ))}
                      </Stack>
                    </CardContent>
                  </Card>
                </Grid>

                <Grid item xs={12} md={6}>
                  <Card sx={{ height: '100%' }}>
                    <CardContent>
                      <Stack direction="row" spacing={1} alignItems="center" mb={2}>
                        <Memory color="primary" fontSize="small" />
                        <Typography variant="h6">Заявленные возможности</Typography>
                      </Stack>

                      {reportedCapabilities.length === 0 ? (
                        <Alert severity="info">
                          API агента пока не возвращает runtime-возможности для этого агента. Панель заполнится автоматически после публикации этих данных в серверной части.
                        </Alert>
                      ) : (
                        <Stack spacing={1.25}>
                          {reportedCapabilities.map((cap) => (
                            <Paper key={cap.key} variant="outlined" sx={{ p: 1.25 }}>
                              <Stack direction="row" justifyContent="space-between" alignItems="center" gap={1}>
                                <Box>
                                  <Typography variant="body2" sx={{ fontWeight: 600 }}>{cap.label}</Typography>
                                  {cap.detail && (
                                    <Typography variant="caption" color="text.secondary">{cap.detail}</Typography>
                                  )}
                                </Box>
                                <Chip
                                  size="small"
                                  color={cap.enabled ? 'success' : 'default'}
                                  label={cap.enabled ? 'Доступно' : 'Недоступно'}
                                  variant={cap.enabled ? 'filled' : 'outlined'}
                                />
                              </Stack>
                            </Paper>
                          ))}
                        </Stack>
                      )}
                    </CardContent>
                  </Card>
                </Grid>
              </Grid>

              <Card>
                <CardContent>
                  <Box display="flex" justifyContent="space-between" alignItems="center" gap={2} flexWrap="wrap" mb={2}>
                    <Typography variant="h6">Версии политики</Typography>
                    <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap">
                      <Typography variant="body2" color="text.secondary">
                        {policyVersionsTotal} версий
                      </Typography>
                      <Button variant="outlined" size="small" startIcon={<Refresh />} onClick={hardRefresh} disabled={actionLoading}>
                        Обновить
                      </Button>
                    </Stack>
                  </Box>

                  <TableContainer component={Paper} variant="outlined">
                    <Table size="small">
                      <TableHead>
                        <TableRow>
                          <TableCell>ID</TableCell>
                          <TableCell>Версия политики</TableCell>
                          <TableCell>Изменение</TableCell>
                          <TableCell>Кем изменено</TableCell>
                          <TableCell>Создано</TableCell>
                          <TableCell align="right">Действие</TableCell>
                        </TableRow>
                      </TableHead>
                      <TableBody>
                        {policyVersions.length === 0 ? (
                          <TableRow>
                            <TableCell colSpan={6}>
                              <Alert severity="info">Версии политики пока не зафиксированы.</Alert>
                            </TableCell>
                          </TableRow>
                        ) : policyVersions.map((version) => (
                          <TableRow key={version.id} hover>
                            <TableCell>#{version.id}</TableCell>
                            <TableCell>{version.policyVersion || '—'}</TableCell>
                            <TableCell>
                              <Chip
                                size="small"
                                label={version.changeType || 'update'}
                                color={
                                  version.changeType === 'delete'
                                    ? 'warning'
                                    : version.changeType === 'rollback'
                                      ? 'info'
                                      : version.changeType === 'create'
                                        ? 'success'
                                        : 'default'
                                }
                                variant={version.changeType === 'update' ? 'outlined' : 'filled'}
                              />
                            </TableCell>
                            <TableCell>{version.changedBy || 'система'}</TableCell>
                            <TableCell>{formatDateTime(version.createdAt)}</TableCell>
                            <TableCell align="right">
                              <Tooltip title="Сделать этот снимок политики текущим">
                                <span>
                                  <Button
                                    size="small"
                                    variant="outlined"
                                    onClick={() => handleRestorePolicyVersion(version.id)}
                                    disabled={actionLoading || !selectedAgentId}
                                  >
                                    Восстановить
                                  </Button>
                                </span>
                              </Tooltip>
                            </TableCell>
                          </TableRow>
                        ))}
                      </TableBody>
                    </Table>
                  </TableContainer>

                  <Box mt={2} display="flex" justifyContent="space-between" alignItems="center" gap={2} flexWrap="wrap">
                    <Typography variant="caption" color="text.secondary">
                      Откат создает новую запись версии политики для сохранения аудита.
                    </Typography>
                    <Stack direction="row" spacing={1} alignItems="center">
                      <Button
                        size="small"
                        variant="outlined"
                        onClick={() => setPolicyVersionsPage((p) => Math.max(1, p - 1))}
                        disabled={policyVersionsPage <= 1 || loadingDetails}
                      >
                        Назад
                      </Button>
                      <Chip size="small" label={`Страница ${policyVersionsPage} / ${policyVersionPages}`} />
                      <Button
                        size="small"
                        variant="outlined"
                        onClick={() => setPolicyVersionsPage((p) => Math.min(policyVersionPages, p + 1))}
                        disabled={policyVersionsPage >= policyVersionPages || loadingDetails}
                      >
                        Вперед
                      </Button>
                    </Stack>
                  </Box>
                </CardContent>
              </Card>

              <Card>
                <CardContent>
                  <Box display="flex" justifyContent="space-between" alignItems="center" mb={2} gap={2} flexWrap="wrap">
                    <Typography variant="h6">Управление</Typography>
                    <Stack direction="row" spacing={1} flexWrap="wrap">
                      <Button
                        variant="outlined"
                        color="error"
                        startIcon={<Lock />}
                        onClick={() => handleQuickBlock(true)}
                        disabled={actionLoading || !selectedAgentId}
                      >
                        Заблокировать ПК
                      </Button>
                      <Button
                        variant="outlined"
                        color="success"
                        startIcon={<LockOpen />}
                        onClick={() => handleQuickBlock(false)}
                        disabled={actionLoading || !selectedAgentId}
                      >
                        Разблокировать ПК
                      </Button>
                      <Tooltip title="Удалить регистрацию агента (не удаляет ПО на endpoint)">
                        <span>
                          <Button
                            variant="text"
                            color="error"
                            onClick={handleDeleteAgent}
                            disabled={actionLoading || !selectedAgentId}
                          >
                            Удалить агента
                          </Button>
                        </span>
                      </Tooltip>
                    </Stack>
                  </Box>

                  <Grid container spacing={2}>
                    <Grid item xs={12} md={4}>
                      <TextField
                        fullWidth
                        label="Причина администратора"
                        value={adminReason}
                        onChange={(e) => setAdminReason(e.target.value)}
                        placeholder="Заблокировано администратором"
                      />
                    </Grid>
                    <Grid item xs={12} md={4}>
                      <TextField
                        fullWidth
                        label="Тип команды"
                        value={customCommandType}
                        onChange={(e) => setCustomCommandType(e.target.value)}
                        placeholder="Например: PING"
                      />
                    </Grid>
                    <Grid item xs={12} md={4}>
                      <Button
                        fullWidth
                        sx={{ height: '100%' }}
                        variant="contained"
                        startIcon={<Send />}
                        onClick={handleSendCustomCommand}
                        disabled={actionLoading || !selectedAgentId}
                      >
                        Отправить команду
                      </Button>
                    </Grid>
                    <Grid item xs={12}>
                      <TextField
                        fullWidth
                        label="JSON полезной нагрузки команды"
                        value={customCommandPayload}
                        onChange={(e) => setCustomCommandPayload(e.target.value)}
                        multiline
                        minRows={4}
                        maxRows={10}
                        inputProps={{ style: { fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace' } }}
                      />
                    </Grid>
                  </Grid>
                </CardContent>
              </Card>

              <Card>
                <CardContent>
                  <Box display="flex" justifyContent="space-between" alignItems="center" gap={2} flexWrap="wrap" mb={2}>
                    <Typography variant="h6">История команд</Typography>
                    <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap">
                      <FormControl size="small" sx={{ minWidth: 170 }}>
                        <InputLabel>Статус</InputLabel>
                        <Select
                          label="Статус"
                          value={commandStatusFilter}
                          onChange={(e) => setCommandStatusFilter(e.target.value)}
                        >
                          <MenuItem value="all">Все статусы</MenuItem>
                          <MenuItem value="pending">В очереди</MenuItem>
                          <MenuItem value="running">Выполняется</MenuItem>
                          <MenuItem value="success">Успешно</MenuItem>
                          <MenuItem value="failed">Ошибка</MenuItem>
                          <MenuItem value="timeout">Таймаут</MenuItem>
                          <MenuItem value="deadletter">В карантине (DLQ)</MenuItem>
                          <MenuItem value="ignored">Игнорировано</MenuItem>
                        </Select>
                      </FormControl>
                      <FormControl size="small" sx={{ minWidth: 210 }}>
                        <InputLabel>Тип команды</InputLabel>
                        <Select
                          label="Тип команды"
                          value={commandTypeFilter}
                          onChange={(e) => setCommandTypeFilter(e.target.value)}
                        >
                          <MenuItem value="all">Все типы</MenuItem>
                          <MenuItem value="PING">PING</MenuItem>
                          <MenuItem value="REFRESH_POLICY">REFRESH_POLICY</MenuItem>
                          <MenuItem value="BLOCK_WORKSTATION">BLOCK_WORKSTATION</MenuItem>
                          <MenuItem value="UNBLOCK_WORKSTATION">UNBLOCK_WORKSTATION</MenuItem>
                          <MenuItem value="SET_COLLECTION_STATE">SET_COLLECTION_STATE</MenuItem>
                          <MenuItem value="SET_LOG_LEVEL">SET_LOG_LEVEL</MenuItem>
                        </Select>
                      </FormControl>
                      <TextField
                        size="small"
                        label="С даты"
                        type="datetime-local"
                        value={commandFromFilter}
                        onChange={(e) => setCommandFromFilter(e.target.value)}
                        InputLabelProps={{ shrink: true }}
                      />
                      <TextField
                        size="small"
                        label="По дату"
                        type="datetime-local"
                        value={commandToFilter}
                        onChange={(e) => setCommandToFilter(e.target.value)}
                        InputLabelProps={{ shrink: true }}
                      />
                      <Button
                        variant="outlined"
                        startIcon={<Refresh />}
                        onClick={hardRefresh}
                        disabled={actionLoading}
                      >
                        Обновить
                      </Button>
                    </Stack>
                  </Box>

                  <TableContainer component={Paper} variant="outlined">
                    <Table size="small">
                      <TableHead>
                        <TableRow>
                          <TableCell>ID</TableCell>
                          <TableCell>Тип</TableCell>
                          <TableCell>Статус</TableCell>
                          <TableCell>Кем запрошено</TableCell>
                          <TableCell>Создано</TableCell>
                          <TableCell>Подтверждено</TableCell>
                          <TableCell>Результат</TableCell>
                          <TableCell align="right">Действия</TableCell>
                        </TableRow>
                      </TableHead>
                      <TableBody>
                        {commands.length === 0 ? (
                          <TableRow>
                            <TableCell colSpan={8}>
                              <Alert severity="info">Для выбранного фильтра команды не найдены.</Alert>
                            </TableCell>
                          </TableRow>
                        ) : commands.map((cmd) => (
                          <TableRow key={cmd.id} hover>
                            <TableCell>#{cmd.id}</TableCell>
                            <TableCell>
                              <Stack direction="row" spacing={1} alignItems="center">
                                <Terminal fontSize="small" color="action" />
                                <Box>
                                  <Typography variant="body2" sx={{ fontWeight: 600 }}>{cmd.type}</Typography>
                                  <Typography variant="caption" color="text.secondary">
                                    {cmd.payloadJson ? prettyJson(cmd.payloadJson).slice(0, 80) : '—'}
                                  </Typography>
                                </Box>
                              </Stack>
                            </TableCell>
                            <TableCell>
                              <Stack direction="row" spacing={0.75} alignItems="center">
                                {getCommandStatusIcon(cmd.status)}
                                <Chip size="small" label={cmd.status || 'неизвестно'} color={getStatusColor(cmd.status)} />
                              </Stack>
                            </TableCell>
                            <TableCell>{cmd.requestedBy || '—'}</TableCell>
                            <TableCell>{formatDateTime(cmd.createdAt)}</TableCell>
                            <TableCell>{formatDateTime(cmd.acknowledgedAt)}</TableCell>
                            <TableCell>
                              <Typography variant="body2" color="text.secondary">
                                {cmd.resultMessage || '—'}
                              </Typography>
                            </TableCell>
                            <TableCell align="right">
                              {canRetryCommand(cmd.status) ? (
                                <Tooltip title="Поставить команду в очередь повторно">
                                  <span>
                                    <Button
                                      size="small"
                                      variant="outlined"
                                      startIcon={<Replay fontSize="small" />}
                                      onClick={() => handleRetryCommand(cmd.id)}
                                      disabled={actionLoading}
                                    >
                                      Повторить
                                    </Button>
                                  </span>
                                </Tooltip>
                              ) : (
                                '—'
                              )}
                            </TableCell>
                          </TableRow>
                        ))}
                      </TableBody>
                    </Table>
                  </TableContainer>

                  <Box mt={2} display="flex" justifyContent="space-between" alignItems="center" gap={2} flexWrap="wrap">
                    <Typography variant="body2" color="text.secondary">
                      {commandsTotal} команд всего
                    </Typography>
                    <Stack direction="row" spacing={1} alignItems="center">
                      <Button
                        size="small"
                        variant="outlined"
                        onClick={() => setCommandPage((p) => Math.max(1, p - 1))}
                        disabled={commandPage <= 1 || loadingDetails}
                      >
                        Назад
                      </Button>
                      <Chip size="small" label={`Страница ${commandPage} / ${commandPages}`} />
                      <Button
                        size="small"
                        variant="outlined"
                        onClick={() => setCommandPage((p) => Math.min(commandPages, p + 1))}
                        disabled={commandPage >= commandPages || loadingDetails}
                      >
                        Вперед
                      </Button>
                    </Stack>
                  </Box>
                </CardContent>
              </Card>
            </Stack>
          )}
        </Grid>
      </Grid>
    </Box>
  );
};

export default Agents;
