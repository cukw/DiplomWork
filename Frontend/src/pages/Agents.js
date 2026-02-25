import React, { useEffect, useMemo, useState } from 'react';
import { alpha, useTheme } from '@mui/material/styles';
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
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
} from '@mui/icons-material';
import { agentAPI } from '../services/api';

const FETCH_PAGE_SIZE = 500;
const COMMAND_PAGE_SIZE = 20;

const formatDateTime = (value) => {
  if (!value) return '—';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return String(value);
  return date.toLocaleString();
};

const formatRelative = (value) => {
  if (!value) return '—';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '—';
  const diffMs = Date.now() - date.getTime();
  const diffSec = Math.floor(diffMs / 1000);
  if (diffSec < 60) return `${diffSec}s ago`;
  const diffMin = Math.floor(diffSec / 60);
  if (diffMin < 60) return `${diffMin}m ago`;
  const diffHour = Math.floor(diffMin / 60);
  if (diffHour < 24) return `${diffHour}h ago`;
  return `${Math.floor(diffHour / 24)}d ago`;
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

const toBool = (value) => (value === true ? true : value === false ? false : null);

const buildEffectiveCapabilities = (policy) => {
  if (!policy) return [];

  return [
    {
      key: 'processes',
      label: 'Process collection',
      enabled: toBool(policy.enableProcessCollection),
      detail: policy.processSnapshotLimit ? `Limit ${policy.processSnapshotLimit}` : null,
    },
    {
      key: 'browser',
      label: 'Browser history',
      enabled: toBool(policy.enableBrowserCollection),
      detail: Array.isArray(policy.browsers) && policy.browsers.length > 0 ? policy.browsers.join(', ') : null,
    },
    {
      key: 'window',
      label: 'Active window tracking',
      enabled: toBool(policy.enableActiveWindowCollection),
      detail: null,
    },
    {
      key: 'idle',
      label: 'Idle time tracking',
      enabled: toBool(policy.enableIdleCollection),
      detail: policy.idleThresholdSec ? `Threshold ${policy.idleThresholdSec}s` : null,
    },
    {
      key: 'autolock',
      label: 'Auto lock on high risk',
      enabled: toBool(policy.autoLockEnabled),
      detail: policy.highRiskThreshold != null ? `Risk ≥ ${policy.highRiskThreshold}` : null,
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

const normalizeAgent = (agent) => ({
  id: agent.id,
  computerId: agent.computerId,
  version: agent.version || '—',
  status: agent.status || 'unknown',
  lastHeartbeat: agent.lastHeartbeat,
  configVersion: agent.configVersion || '—',
  offlineSince: agent.offlineSince,
  capabilities: agent.capabilities,
  reportedCapabilities: agent.reportedCapabilities,
  metadata: agent.metadata,
});

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
  const [commandPage, setCommandPage] = useState(1);
  const [policyVersionsPage, setPolicyVersionsPage] = useState(1);

  const [customCommandType, setCustomCommandType] = useState('PING');
  const [customCommandPayload, setCustomCommandPayload] = useState('{}');
  const [adminReason, setAdminReason] = useState('Blocked by admin');

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

        setSelectedAgentId((prev) => {
          if (prev && rows.some((a) => a.id === prev)) return prev;
          return rows[0]?.id ?? null;
        });
      } catch (err) {
        if (!alive) return;
        setError(err?.response?.data?.message || err?.message || 'Failed to load agents');
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
  }, [selectedAgentId, commandStatusFilter]);

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
          agentAPI.getAgentCommands(selectedAgentId, {
            page: commandPage,
            pageSize: COMMAND_PAGE_SIZE,
            ...(commandStatusFilter !== 'all' ? { status: commandStatusFilter } : {}),
          }),
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
        setError(err?.response?.data?.message || err?.message || 'Failed to load agent details');
      } finally {
        if (alive) setLoadingDetails(false);
      }
    };

    fetchDetails();
    return () => { alive = false; };
  }, [selectedAgentId, commandStatusFilter, commandPage, policyVersionsPage]);

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

  const effectiveCapabilities = useMemo(() => buildEffectiveCapabilities(policy), [policy]);
  const reportedCapabilities = useMemo(() => extractReportedCapabilities(selectedAgent), [selectedAgent]);

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
      if (selectedAgentId && rows.some((a) => a.id === selectedAgentId)) {
        const [agentResp, policyResp, policyVersionsResp, commandsResp] = await Promise.all([
          agentAPI.getAgentById(selectedAgentId),
          agentAPI.getAgentPolicy(selectedAgentId),
          agentAPI.getAgentPolicyVersions(selectedAgentId, {
            page: policyVersionsPage,
            pageSize: 10,
          }),
          agentAPI.getAgentCommands(selectedAgentId, {
            page: commandPage,
            pageSize: COMMAND_PAGE_SIZE,
            ...(commandStatusFilter !== 'all' ? { status: commandStatusFilter } : {}),
          }),
        ]);
        setSelectedAgent(normalizeAgent(agentResp));
        setPolicy(policyResp || null);
        setPolicyVersions(policyVersionsResp?.versions || []);
        setPolicyVersionsTotal(policyVersionsResp?.totalCount || 0);
        setCommands(commandsResp?.commands || []);
        setCommandsTotal(commandsResp?.totalCount || 0);
      }
      setSuccess('Agent inventory refreshed');
      clearSuccessLater();
    } catch (err) {
      setError(err?.response?.data?.message || err?.message || 'Refresh failed');
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
        await agentAPI.blockWorkstation(selectedAgentId, adminReason || 'Blocked by admin');
        setSuccess('Block command queued');
      } else {
        await agentAPI.unblockWorkstation(selectedAgentId, adminReason || 'Unblocked by admin');
        setSuccess('Unblock command queued');
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
        agentAPI.getAgentCommands(selectedAgentId, {
          page: 1,
          pageSize: COMMAND_PAGE_SIZE,
          ...(commandStatusFilter !== 'all' ? { status: commandStatusFilter } : {}),
        }),
      ]);
      setSelectedAgent(normalizeAgent(agentResp));
      setPolicy(policyResp || null);
      setPolicyVersions(policyVersionsResp?.versions || []);
      setPolicyVersionsTotal(policyVersionsResp?.totalCount || 0);
      setCommands(commandsResp?.commands || []);
      setCommandsTotal(commandsResp?.totalCount || 0);
    } catch (err) {
      setError(err?.response?.data?.message || err?.message || 'Failed to queue command');
    } finally {
      setActionLoading(false);
    }
  };

  const handleSendCustomCommand = async () => {
    if (!selectedAgentId) return;

    try {
      JSON.parse(customCommandPayload || '{}');
    } catch {
      setError('Payload JSON is invalid');
      return;
    }

    try {
      setActionLoading(true);
      setError(null);
      const command = await agentAPI.createAgentCommand(selectedAgentId, {
        type: String(customCommandType || '').trim().toUpperCase(),
        payloadJson: customCommandPayload || '{}',
      });
      setSuccess(`Command queued: ${command?.type || customCommandType}`);
      clearSuccessLater();
      setCommandPage(1);
      const commandsResp = await agentAPI.getAgentCommands(selectedAgentId, {
        page: 1,
        pageSize: COMMAND_PAGE_SIZE,
        ...(commandStatusFilter !== 'all' ? { status: commandStatusFilter } : {}),
      });
      setCommands(commandsResp?.commands || []);
      setCommandsTotal(commandsResp?.totalCount || 0);
    } catch (err) {
      setError(err?.response?.data?.message || err?.message || 'Failed to create command');
    } finally {
      setActionLoading(false);
    }
  };

  const handleDeleteAgent = async () => {
    if (!selectedAgentId) return;
    const confirmed = window.confirm(`Delete agent #${selectedAgentId}? This action cannot be undone.`);
    if (!confirmed) return;

    try {
      setActionLoading(true);
      setError(null);
      await agentAPI.deleteAgent(selectedAgentId);
      setSuccess(`Agent #${selectedAgentId} deleted`);
      clearSuccessLater();
      const nextAgents = agents.filter((a) => a.id !== selectedAgentId);
      setAgents(nextAgents);
      setSelectedAgentId(nextAgents[0]?.id ?? null);
    } catch (err) {
      setError(err?.response?.data?.message || err?.message || 'Failed to delete agent');
    } finally {
      setActionLoading(false);
    }
  };

  const handleRestorePolicyVersion = async (versionId) => {
    if (!selectedAgentId || !versionId) return;
    const confirmed = window.confirm(`Restore policy version #${versionId} for agent #${selectedAgentId}?`);
    if (!confirmed) return;

    try {
      setActionLoading(true);
      setError(null);
      const result = await agentAPI.restoreAgentPolicyVersion(selectedAgentId, versionId, {});
      setPolicy(result?.policy || null);
      setSuccess(result?.message || `Policy version #${versionId} restored`);
      clearSuccessLater();

      const versionsResp = await agentAPI.getAgentPolicyVersions(selectedAgentId, {
        page: policyVersionsPage,
        pageSize: 10,
      });
      setPolicyVersions(versionsResp?.versions || []);
      setPolicyVersionsTotal(versionsResp?.totalCount || 0);
    } catch (err) {
      setError(err?.response?.data?.message || err?.message || 'Failed to restore policy version');
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
          <Typography variant="h4">Agents</Typography>
          <Typography variant="body2" color="text.secondary">
            Agent inventory, effective capabilities and command history
          </Typography>
        </Box>
        <Button
          variant="contained"
          startIcon={actionLoading ? <CircularProgress size={16} color="inherit" /> : <Refresh />}
          onClick={hardRefresh}
          disabled={actionLoading}
        >
          Refresh
        </Button>
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
                  placeholder="Search agents"
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
                  <InputLabel>Status</InputLabel>
                  <Select
                    label="Status"
                    value={statusFilter}
                    onChange={(e) => setStatusFilter(e.target.value)}
                  >
                    <MenuItem value="all">All</MenuItem>
                    <MenuItem value="online">Online</MenuItem>
                    <MenuItem value="offline">Offline</MenuItem>
                    <MenuItem value="active">Active</MenuItem>
                    <MenuItem value="error">Error</MenuItem>
                  </Select>
                </FormControl>

                <Divider />

                {loadingList ? (
                  <Box display="flex" justifyContent="center" py={4}><CircularProgress /></Box>
                ) : filteredAgents.length === 0 ? (
                  <Alert severity="info">No agents found</Alert>
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
                          <ListItemIcon sx={{ minWidth: 38, mt: 0.3 }}>
                            <Computer color={selected ? 'primary' : 'action'} fontSize="small" />
                          </ListItemIcon>
                          <ListItemText
                            primary={
                              <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap">
                                <Typography variant="subtitle2">Agent #{agent.id}</Typography>
                                <Chip size="small" label={agent.status || 'unknown'} color={getStatusColor(agent.status)} />
                              </Stack>
                            }
                            secondary={
                              <Box mt={0.5}>
                                <Typography variant="caption" display="block" color="text.secondary">
                                  Computer #{agent.computerId} • v{agent.version}
                                </Typography>
                                <Typography variant="caption" display="block" color="text.secondary">
                                  Last heartbeat: {formatRelative(agent.lastHeartbeat)}
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
            <Alert severity="info">Select an agent to view details.</Alert>
          ) : loadingDetails && !selectedAgent ? (
            <Box display="flex" justifyContent="center" py={8}><CircularProgress /></Box>
          ) : (
            <Stack spacing={3}>
              <Card>
                <CardContent>
                  <Box display="flex" justifyContent="space-between" alignItems="flex-start" gap={2} flexWrap="wrap">
                    <Box>
                      <Typography variant="h5">Agent #{selectedAgent?.id}</Typography>
                      <Typography variant="body2" color="text.secondary">
                        Computer #{selectedAgent?.computerId} • Version {selectedAgent?.version || '—'} • Config {selectedAgent?.configVersion || '—'}
                      </Typography>
                    </Box>
                    <Stack direction="row" spacing={1} flexWrap="wrap">
                      <Chip label={selectedAgent?.status || 'unknown'} color={getStatusColor(selectedAgent?.status)} />
                      {policy?.adminBlocked ? (
                        <Chip label="Admin blocked" color="error" variant="filled" />
                      ) : (
                        <Chip label="Not blocked" color="success" variant="outlined" />
                      )}
                    </Stack>
                  </Box>

                  <Grid container spacing={2} sx={{ mt: 1 }}>
                    <Grid item xs={12} sm={6} md={3}>
                      <Paper variant="outlined" sx={{ p: 1.5 }}>
                        <Typography variant="caption" color="text.secondary">Last heartbeat</Typography>
                        <Typography variant="body2">{formatDateTime(selectedAgent?.lastHeartbeat)}</Typography>
                      </Paper>
                    </Grid>
                    <Grid item xs={12} sm={6} md={3}>
                      <Paper variant="outlined" sx={{ p: 1.5 }}>
                        <Typography variant="caption" color="text.secondary">Offline since</Typography>
                        <Typography variant="body2">{formatDateTime(selectedAgent?.offlineSince)}</Typography>
                      </Paper>
                    </Grid>
                    <Grid item xs={12} sm={6} md={3}>
                      <Paper variant="outlined" sx={{ p: 1.5 }}>
                        <Typography variant="caption" color="text.secondary">Policy version</Typography>
                        <Typography variant="body2">{policy?.policyVersion || '—'}</Typography>
                      </Paper>
                    </Grid>
                    <Grid item xs={12} sm={6} md={3}>
                      <Paper variant="outlined" sx={{ p: 1.5 }}>
                        <Typography variant="caption" color="text.secondary">Policy updated</Typography>
                        <Typography variant="body2">{formatDateTime(policy?.updatedAt)}</Typography>
                      </Paper>
                    </Grid>
                  </Grid>

                  {policy?.adminBlocked && policy?.blockedReason && (
                    <Alert severity="warning" sx={{ mt: 2 }}>{policy.blockedReason}</Alert>
                  )}
                </CardContent>
              </Card>

              <Grid container spacing={3}>
                <Grid item xs={12} md={6}>
                  <Card sx={{ height: '100%' }}>
                    <CardContent>
                      <Stack direction="row" spacing={1} alignItems="center" mb={2}>
                        <Policy color="primary" fontSize="small" />
                        <Typography variant="h6">Effective Capabilities (Policy)</Typography>
                      </Stack>
                      <Stack spacing={1.25}>
                        {effectiveCapabilities.length === 0 ? (
                          <Alert severity="info">No policy loaded yet.</Alert>
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
                                label={cap.enabled ? 'Enabled' : 'Disabled'}
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
                        <Typography variant="h6">Reported Capabilities</Typography>
                      </Stack>

                      {reportedCapabilities.length === 0 ? (
                        <Alert severity="info">
                          Agent API does not yet return runtime capabilities for this agent. This panel will populate automatically when the backend exposes them.
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
                                  label={cap.enabled ? 'Available' : 'Unavailable'}
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
                    <Typography variant="h6">Policy Versions</Typography>
                    <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap">
                      <Typography variant="body2" color="text.secondary">
                        {policyVersionsTotal} version(s)
                      </Typography>
                      <Button variant="outlined" size="small" startIcon={<Refresh />} onClick={hardRefresh} disabled={actionLoading}>
                        Refresh
                      </Button>
                    </Stack>
                  </Box>

                  <TableContainer component={Paper} variant="outlined">
                    <Table size="small">
                      <TableHead>
                        <TableRow>
                          <TableCell>ID</TableCell>
                          <TableCell>Policy Version</TableCell>
                          <TableCell>Change</TableCell>
                          <TableCell>Changed By</TableCell>
                          <TableCell>Created</TableCell>
                          <TableCell align="right">Action</TableCell>
                        </TableRow>
                      </TableHead>
                      <TableBody>
                        {policyVersions.length === 0 ? (
                          <TableRow>
                            <TableCell colSpan={6}>
                              <Alert severity="info">No policy versions recorded yet.</Alert>
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
                            <TableCell>{version.changedBy || 'system'}</TableCell>
                            <TableCell>{formatDateTime(version.createdAt)}</TableCell>
                            <TableCell align="right">
                              <Tooltip title="Restore this policy snapshot as the current policy">
                                <span>
                                  <Button
                                    size="small"
                                    variant="outlined"
                                    onClick={() => handleRestorePolicyVersion(version.id)}
                                    disabled={actionLoading || !selectedAgentId}
                                  >
                                    Restore
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
                      Rollback creates a new policy version entry to preserve audit history.
                    </Typography>
                    <Stack direction="row" spacing={1} alignItems="center">
                      <Button
                        size="small"
                        variant="outlined"
                        onClick={() => setPolicyVersionsPage((p) => Math.max(1, p - 1))}
                        disabled={policyVersionsPage <= 1 || loadingDetails}
                      >
                        Prev
                      </Button>
                      <Chip size="small" label={`Page ${policyVersionsPage} / ${policyVersionPages}`} />
                      <Button
                        size="small"
                        variant="outlined"
                        onClick={() => setPolicyVersionsPage((p) => Math.min(policyVersionPages, p + 1))}
                        disabled={policyVersionsPage >= policyVersionPages || loadingDetails}
                      >
                        Next
                      </Button>
                    </Stack>
                  </Box>
                </CardContent>
              </Card>

              <Card>
                <CardContent>
                  <Box display="flex" justifyContent="space-between" alignItems="center" mb={2} gap={2} flexWrap="wrap">
                    <Typography variant="h6">Control Plane</Typography>
                    <Stack direction="row" spacing={1} flexWrap="wrap">
                      <Button
                        variant="outlined"
                        color="error"
                        startIcon={<Lock />}
                        onClick={() => handleQuickBlock(true)}
                        disabled={actionLoading || !selectedAgentId}
                      >
                        Block PC
                      </Button>
                      <Button
                        variant="outlined"
                        color="success"
                        startIcon={<LockOpen />}
                        onClick={() => handleQuickBlock(false)}
                        disabled={actionLoading || !selectedAgentId}
                      >
                        Unblock PC
                      </Button>
                      <Tooltip title="Delete agent registration (does not uninstall endpoint software)">
                        <span>
                          <Button
                            variant="text"
                            color="error"
                            onClick={handleDeleteAgent}
                            disabled={actionLoading || !selectedAgentId}
                          >
                            Delete Agent
                          </Button>
                        </span>
                      </Tooltip>
                    </Stack>
                  </Box>

                  <Grid container spacing={2}>
                    <Grid item xs={12} md={4}>
                      <TextField
                        fullWidth
                        label="Admin reason"
                        value={adminReason}
                        onChange={(e) => setAdminReason(e.target.value)}
                        placeholder="Blocked by admin"
                      />
                    </Grid>
                    <Grid item xs={12} md={4}>
                      <TextField
                        fullWidth
                        label="Command type"
                        value={customCommandType}
                        onChange={(e) => setCustomCommandType(e.target.value)}
                        placeholder="PING"
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
                        Send Command
                      </Button>
                    </Grid>
                    <Grid item xs={12}>
                      <TextField
                        fullWidth
                        label="Command payload JSON"
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
                    <Typography variant="h6">Command History</Typography>
                    <Stack direction="row" spacing={1} alignItems="center">
                      <FormControl size="small" sx={{ minWidth: 170 }}>
                        <InputLabel>Status</InputLabel>
                        <Select
                          label="Status"
                          value={commandStatusFilter}
                          onChange={(e) => setCommandStatusFilter(e.target.value)}
                        >
                          <MenuItem value="all">All statuses</MenuItem>
                          <MenuItem value="pending">Pending</MenuItem>
                          <MenuItem value="running">Running</MenuItem>
                          <MenuItem value="success">Success</MenuItem>
                          <MenuItem value="failed">Failed</MenuItem>
                          <MenuItem value="ignored">Ignored</MenuItem>
                        </Select>
                      </FormControl>
                      <Button variant="outlined" startIcon={<Refresh />} onClick={hardRefresh} disabled={actionLoading}>Refresh</Button>
                    </Stack>
                  </Box>

                  <TableContainer component={Paper} variant="outlined">
                    <Table size="small">
                      <TableHead>
                        <TableRow>
                          <TableCell>ID</TableCell>
                          <TableCell>Type</TableCell>
                          <TableCell>Status</TableCell>
                          <TableCell>Requested By</TableCell>
                          <TableCell>Created</TableCell>
                          <TableCell>Acked</TableCell>
                          <TableCell>Result</TableCell>
                        </TableRow>
                      </TableHead>
                      <TableBody>
                        {commands.length === 0 ? (
                          <TableRow>
                            <TableCell colSpan={7}>
                              <Alert severity="info">No commands found for current filter.</Alert>
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
                                    {cmd.payloadJson ? prettyJson(cmd.payloadJson).slice(0, 80) : '{}'}
                                  </Typography>
                                </Box>
                              </Stack>
                            </TableCell>
                            <TableCell>
                              <Stack direction="row" spacing={0.75} alignItems="center">
                                {getCommandStatusIcon(cmd.status)}
                                <Chip size="small" label={cmd.status || 'unknown'} color={getStatusColor(cmd.status)} />
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
                          </TableRow>
                        ))}
                      </TableBody>
                    </Table>
                  </TableContainer>

                  <Box mt={2} display="flex" justifyContent="space-between" alignItems="center" gap={2} flexWrap="wrap">
                    <Typography variant="body2" color="text.secondary">
                      {commandsTotal} command(s) total
                    </Typography>
                    <Stack direction="row" spacing={1} alignItems="center">
                      <Button
                        size="small"
                        variant="outlined"
                        onClick={() => setCommandPage((p) => Math.max(1, p - 1))}
                        disabled={commandPage <= 1 || loadingDetails}
                      >
                        Prev
                      </Button>
                      <Chip size="small" label={`Page ${commandPage} / ${commandPages}`} />
                      <Button
                        size="small"
                        variant="outlined"
                        onClick={() => setCommandPage((p) => Math.min(commandPages, p + 1))}
                        disabled={commandPage >= commandPages || loadingDetails}
                      >
                        Next
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
