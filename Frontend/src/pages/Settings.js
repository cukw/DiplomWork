import React, { useState, useEffect } from 'react';
import {
  Box,
  Card,
  CardContent,
  Typography,
  Paper,
  Grid,
  Button,
  TextField,
  Switch,
  FormControlLabel,
  Alert,
  Dialog,
  DialogTitle,
  DialogContent,
  DialogActions,
  List,
  ListItem,
  ListItemText,
  ListItemSecondaryAction,
  IconButton,
  Chip,
  Divider,
  LinearProgress,
  Stack,
  Tabs,
  Tab,
  Select,
  MenuItem,
  FormControl,
  InputLabel,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Tooltip
} from '@mui/material';
import {
  Save,
  Add,
  Delete,
  Edit,
  Security,
  Notifications,
  Settings as SettingsIcon,
  Storage,
  NetworkCheck,
  Refresh
} from '@mui/icons-material';
import { useAuth } from '../contexts/AuthContext';
import { useNotifications } from '../contexts/NotificationContext';
import { agentAPI, systemAPI, alertRulesAPI, settingsAPI } from '../services/api';

const DEFAULT_ALERT_RULE_FORM = {
  name: '',
  enabled: true,
  severity: 'medium',
  metric: 'anomaly_count',
  operator: 'gte',
  threshold: 5,
  windowMinutes: 15,
  activityType: '',
  userId: '',
  computerId: '',
  notifyInApp: true,
  notifyEmail: false,
  cooldownMinutes: 10,
};

const ALERT_RULE_LABELS = {
  anomaly_count: 'Anomaly Count',
  blocked_activities: 'Blocked Activities',
  average_risk_score: 'Average Risk Score',
  total_activities: 'Total Activities',
};

const OPERATOR_LABELS = {
  gt: '>',
  gte: '>=',
  lt: '<',
  lte: '<=',
  eq: '=',
};

const DEFAULT_AGENT_POLICY_FORM = {
  collectionIntervalSec: 5,
  heartbeatIntervalSec: 15,
  flushIntervalSec: 5,
  enableProcessCollection: true,
  enableBrowserCollection: true,
  enableActiveWindowCollection: true,
  enableIdleCollection: true,
  idleThresholdSec: 120,
  browserPollIntervalSec: 10,
  processSnapshotLimit: 50,
  highRiskThreshold: 85,
  autoLockEnabled: true,
  adminBlocked: false,
  blockedReason: '',
  browsersCsv: 'chrome, edge, firefox',
};

const DEFAULT_AGENT_COMMAND_FORM = {
  type: 'PING',
  payloadJson: '{}',
  requestedBy: '',
};

const AGENT_COMMAND_TYPES = [
  'PING',
  'REFRESH_POLICY',
  'BLOCK_WORKSTATION',
  'UNBLOCK_WORKSTATION',
  'SET_COLLECTION_STATE',
  'SET_LOG_LEVEL',
];

const AGENT_COMMAND_STATUS_OPTIONS = [
  { value: '', label: 'All' },
  { value: 'pending', label: 'Pending' },
  { value: 'running', label: 'Running' },
  { value: 'success', label: 'Success' },
  { value: 'failed', label: 'Failed' },
  { value: 'ignored', label: 'Ignored' },
];

const mapAgentPolicyToForm = (policy) => ({
  collectionIntervalSec: policy?.collectionIntervalSec ?? 5,
  heartbeatIntervalSec: policy?.heartbeatIntervalSec ?? 15,
  flushIntervalSec: policy?.flushIntervalSec ?? 5,
  enableProcessCollection: policy?.enableProcessCollection ?? true,
  enableBrowserCollection: policy?.enableBrowserCollection ?? true,
  enableActiveWindowCollection: policy?.enableActiveWindowCollection ?? true,
  enableIdleCollection: policy?.enableIdleCollection ?? true,
  idleThresholdSec: policy?.idleThresholdSec ?? 120,
  browserPollIntervalSec: policy?.browserPollIntervalSec ?? 10,
  processSnapshotLimit: policy?.processSnapshotLimit ?? 50,
  highRiskThreshold: policy?.highRiskThreshold ?? 85,
  autoLockEnabled: policy?.autoLockEnabled ?? true,
  adminBlocked: policy?.adminBlocked ?? false,
  blockedReason: policy?.blockedReason ?? '',
  browsersCsv: Array.isArray(policy?.browsers) && policy.browsers.length > 0
    ? policy.browsers.join(', ')
    : 'chrome, edge, firefox',
});

const buildAgentPolicyPayload = (form) => ({
  collectionIntervalSec: Math.max(1, Number(form.collectionIntervalSec) || 5),
  heartbeatIntervalSec: Math.max(1, Number(form.heartbeatIntervalSec) || 15),
  flushIntervalSec: Math.max(1, Number(form.flushIntervalSec) || 5),
  enableProcessCollection: Boolean(form.enableProcessCollection),
  enableBrowserCollection: Boolean(form.enableBrowserCollection),
  enableActiveWindowCollection: Boolean(form.enableActiveWindowCollection),
  enableIdleCollection: Boolean(form.enableIdleCollection),
  idleThresholdSec: Math.max(5, Number(form.idleThresholdSec) || 120),
  browserPollIntervalSec: Math.max(5, Number(form.browserPollIntervalSec) || 10),
  processSnapshotLimit: Math.max(1, Number(form.processSnapshotLimit) || 50),
  highRiskThreshold: Math.max(0, Math.min(100, Number(form.highRiskThreshold) || 85)),
  autoLockEnabled: Boolean(form.autoLockEnabled),
  adminBlocked: Boolean(form.adminBlocked),
  blockedReason: String(form.blockedReason || '').trim(),
  browsers: String(form.browsersCsv || '')
    .split(',')
    .map((item) => item.trim().toLowerCase())
    .filter(Boolean),
});

const getCommandStatusColor = (status) => {
  const normalized = String(status || '').toLowerCase();
  if (normalized === 'success') return 'success';
  if (normalized === 'pending' || normalized === 'running') return 'warning';
  if (normalized === 'failed') return 'error';
  return 'default';
};

const normalizeListEntries = (entries) => (Array.isArray(entries) ? entries : [])
  .map((entry, index) => ({
    id: Number(entry?.id) > 0 ? Number(entry.id) : Date.now() + index,
    application: String(entry?.application || '').trim(),
    description: String(entry?.description || '').trim(),
  }))
  .filter((entry) => entry.application);

const Settings = () => {
  useAuth();
  const { addNotification } = useNotifications();
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(null);
  const [success, setSuccess] = useState(null);
  const [tabValue, setTabValue] = useState(0);
  const [confirmDialogOpen, setConfirmDialogOpen] = useState(false);
  const [confirmAction, setConfirmAction] = useState(null);
  const [systemHealth, setSystemHealth] = useState(null);
  const [monitoringAgents, setMonitoringAgents] = useState([]);
  const [monitoringDataLoading, setMonitoringDataLoading] = useState(false);
  const [monitoringDataError, setMonitoringDataError] = useState(null);
  const [monitoringLastUpdated, setMonitoringLastUpdated] = useState(null);
  const [selectedMonitoringAgentId, setSelectedMonitoringAgentId] = useState(null);
  const [selectedAgentPolicy, setSelectedAgentPolicy] = useState(null);
  const [agentPolicyForm, setAgentPolicyForm] = useState(DEFAULT_AGENT_POLICY_FORM);
  const [agentControlLoading, setAgentControlLoading] = useState(false);
  const [agentControlError, setAgentControlError] = useState(null);
  const [agentPolicySaving, setAgentPolicySaving] = useState(false);
  const [agentActionLoading, setAgentActionLoading] = useState(false);
  const [agentCommands, setAgentCommands] = useState([]);
  const [agentCommandsTotal, setAgentCommandsTotal] = useState(0);
  const [agentCommandStatusFilter, setAgentCommandStatusFilter] = useState('');
  const [agentCommandForm, setAgentCommandForm] = useState(DEFAULT_AGENT_COMMAND_FORM);
  const [agentCommandSaving, setAgentCommandSaving] = useState(false);
  const [agentAdminReason, setAgentAdminReason] = useState('');
  const [alertRules, setAlertRules] = useState([]);
  const [alertRuleMetadata, setAlertRuleMetadata] = useState(null);
  const [alertRulesLoading, setAlertRulesLoading] = useState(false);
  const [alertRulesError, setAlertRulesError] = useState(null);
  const [alertRuleDialogOpen, setAlertRuleDialogOpen] = useState(false);
  const [editingAlertRuleId, setEditingAlertRuleId] = useState(null);
  const [alertRuleSaving, setAlertRuleSaving] = useState(false);
  const [alertRuleForm, setAlertRuleForm] = useState(DEFAULT_ALERT_RULE_FORM);

  // General Settings
  const [generalSettings, setGeneralSettings] = useState({
    systemName: 'Activity Monitoring System',
    logLevel: 'Info',
    maxLogRetention: '30',
    sessionTimeout: '60',
    enableAuditLog: true
  });

  // Security Settings
  const [securitySettings, setSecuritySettings] = useState({
    passwordMinLength: '8',
    passwordRequireSpecialChars: true,
    sessionTimeoutMinutes: '30',
    maxLoginAttempts: '5',
    lockoutDurationMinutes: '15',
    enableTwoFactor: false,
    jwtExpirationHours: '24'
  });

  // Notification Settings
  const [notificationSettings, setNotificationSettings] = useState({
    emailNotifications: true,
    smsNotifications: false,
    pushNotifications: true,
    alertThreshold: '5',
    notificationEmail: 'admin@company.com',
    smtpServer: 'smtp.company.com',
    smtpPort: '587'
  });

  // Monitoring Settings
  const [monitoringSettings, setMonitoringSettings] = useState({
    dataRetentionDays: '90',
    realTimeMonitoring: true,
    anomalyDetection: true,
    monitoringInterval: '5',
    enableWhitelist: true,
    enableBlacklist: true
  });

  // Whitelist entries
  const [whitelistEntries, setWhitelistEntries] = useState([
    { id: 1, application: 'chrome.exe', description: 'Google Chrome Browser' },
    { id: 2, application: 'explorer.exe', description: 'Windows Explorer' },
    { id: 3, application: 'winword.exe', description: 'Microsoft Word' }
  ]);

  // Blacklist entries
  const [blacklistEntries, setBlacklistEntries] = useState([
    { id: 1, application: 'torrent.exe', description: 'Torrent Client' },
    { id: 2, application: 'game.exe', description: 'Gaming Application' }
  ]);

  useEffect(() => {
    fetchSettings();
  }, []);

  useEffect(() => {
    if (tabValue !== 3) return undefined;

    fetchMonitoringData();

    if (!monitoringSettings.realTimeMonitoring) return undefined;

    const intervalSeconds = Math.max(5, Number(monitoringSettings.monitoringInterval) || 5);
    const timerId = window.setInterval(() => {
      if (document.hidden) return;
      fetchMonitoringData({ silent: true });
    }, intervalSeconds * 1000);

    return () => window.clearInterval(timerId);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tabValue, monitoringSettings.realTimeMonitoring, monitoringSettings.monitoringInterval]);

  useEffect(() => {
    if (tabValue !== 3) return;

    if (!monitoringAgents.length) {
      setSelectedMonitoringAgentId(null);
      setSelectedAgentPolicy(null);
      setAgentPolicyForm(DEFAULT_AGENT_POLICY_FORM);
      setAgentCommands([]);
      setAgentCommandsTotal(0);
      return;
    }

    setSelectedMonitoringAgentId((prev) => {
      if (prev && monitoringAgents.some((agent) => agent.id === prev)) return prev;
      return monitoringAgents[0].id;
    });
  }, [tabValue, monitoringAgents]);

  useEffect(() => {
    if (tabValue !== 3 || !selectedMonitoringAgentId) return;
    fetchAgentControlData(selectedMonitoringAgentId);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tabValue, selectedMonitoringAgentId, agentCommandStatusFilter]);

  useEffect(() => {
    if (tabValue !== 2) return;
    fetchAlertRules();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tabValue]);

  const applySettingsPayload = (payload) => {
    if (!payload || typeof payload !== 'object') return;

    if (payload.generalSettings) {
      setGeneralSettings((prev) => ({ ...prev, ...payload.generalSettings }));
    }
    if (payload.securitySettings) {
      setSecuritySettings((prev) => ({ ...prev, ...payload.securitySettings }));
    }
    if (payload.notificationSettings) {
      setNotificationSettings((prev) => ({ ...prev, ...payload.notificationSettings }));
    }
    if (payload.monitoringSettings) {
      setMonitoringSettings((prev) => ({ ...prev, ...payload.monitoringSettings }));
    }
    if (payload.whitelistEntries) {
      setWhitelistEntries(normalizeListEntries(payload.whitelistEntries));
    }
    if (payload.blacklistEntries) {
      setBlacklistEntries(normalizeListEntries(payload.blacklistEntries));
    }
  };

  const fetchSettings = async ({ silent = false } = {}) => {
    try {
      if (!silent) setLoading(true);
      setError(null);
      const payload = await settingsAPI.getSettings();
      applySettingsPayload(payload);
    } catch (err) {
      setError(err?.response?.data?.message || err?.message || 'Failed to load settings');
      console.error('Settings fetch error:', err);
    } finally {
      setLoading(false);
    }
  };

  const handleTabChange = (event, newValue) => {
    setTabValue(newValue);
  };

  const handleSaveSettings = async (category) => {
    try {
      setLoading(true);
      setError(null);

      const payload = {
        generalSettings,
        securitySettings,
        notificationSettings,
        monitoringSettings,
        whitelistEntries: normalizeListEntries(whitelistEntries),
        blacklistEntries: normalizeListEntries(blacklistEntries),
      };

      const saved = await settingsAPI.saveSettings(payload);
      applySettingsPayload(saved);

      setSuccess(`${category} settings saved successfully`);
      if (typeof addNotification === 'function') {
        addNotification({
          type: 'success',
          title: `${category} settings saved`,
          message: `${category} settings have been updated`,
          timestamp: new Date().toISOString()
        });
      }
      
      setTimeout(() => setSuccess(null), 3000);
    } catch (err) {
      setError(err?.response?.data?.message || err?.message || 'Failed to save settings');
      console.error('Save settings error:', err);
    } finally {
      setLoading(false);
    }
  };

  const handleAddWhitelistEntry = () => {
    const newEntry = {
      id: Date.now(),
      application: '',
      description: ''
    };
    setWhitelistEntries([...whitelistEntries, newEntry]);
  };

  const handleUpdateWhitelistEntry = (id, field, value) => {
    setWhitelistEntries(whitelistEntries.map(entry => 
      entry.id === id ? { ...entry, [field]: value } : entry
    ));
  };

  const handleDeleteWhitelistEntry = (id) => {
    setConfirmAction({
      type: 'delete_whitelist',
      id: id,
      message: 'Are you sure you want to delete this whitelist entry?'
    });
    setConfirmDialogOpen(true);
  };

  const handleAddBlacklistEntry = () => {
    const newEntry = {
      id: Date.now(),
      application: '',
      description: ''
    };
    setBlacklistEntries([...blacklistEntries, newEntry]);
  };

  const handleUpdateBlacklistEntry = (id, field, value) => {
    setBlacklistEntries(blacklistEntries.map(entry => 
      entry.id === id ? { ...entry, [field]: value } : entry
    ));
  };

  const handleDeleteBlacklistEntry = (id) => {
    setConfirmAction({
      type: 'delete_blacklist',
      id: id,
      message: 'Are you sure you want to delete this blacklist entry?'
    });
    setConfirmDialogOpen(true);
  };

  const handleConfirmAction = async () => {
    try {
      if (confirmAction?.type === 'delete_whitelist') {
        setWhitelistEntries(whitelistEntries.filter(entry => entry.id !== confirmAction.id));
      } else if (confirmAction?.type === 'delete_blacklist') {
        setBlacklistEntries(blacklistEntries.filter(entry => entry.id !== confirmAction.id));
      } else if (confirmAction?.type === 'delete_alert_rule') {
        await alertRulesAPI.deleteRule(confirmAction.id);
        setAlertRules((prev) => prev.filter((rule) => rule.id !== confirmAction.id));
      } else if (confirmAction?.type === 'reset_agent_policy') {
        await agentAPI.deleteAgentPolicy(confirmAction.agentId);
        if (confirmAction.agentId) {
          await fetchAgentControlData(confirmAction.agentId, { silent: true });
        }
        setSuccess('Agent policy reset to defaults');
        setTimeout(() => setSuccess(null), 3000);
      }
    } catch (err) {
      setError(err?.response?.data?.message || err?.message || 'Action failed');
    } finally {
      setConfirmDialogOpen(false);
      setConfirmAction(null);
    }
  };

  const handleReloadSettings = async () => {
    await fetchSettings();
    if (tabValue === 3) {
      await fetchMonitoringData({ silent: true });
    }
    setSuccess('Settings reloaded');
    setTimeout(() => setSuccess(null), 2000);
  };

  const normalizeAlertRulePayload = (formState) => ({
    name: String(formState.name || '').trim(),
    enabled: Boolean(formState.enabled),
    severity: String(formState.severity || 'medium').toLowerCase(),
    metric: String(formState.metric || 'anomaly_count').toLowerCase(),
    operator: String(formState.operator || 'gte').toLowerCase(),
    threshold: Number(formState.threshold) || 0,
    windowMinutes: Math.max(1, Number(formState.windowMinutes) || 1),
    activityType: String(formState.activityType || '').trim() || null,
    userId: formState.userId === '' || formState.userId === null ? null : Number(formState.userId),
    computerId: formState.computerId === '' || formState.computerId === null ? null : Number(formState.computerId),
    notifyInApp: Boolean(formState.notifyInApp),
    notifyEmail: Boolean(formState.notifyEmail),
    cooldownMinutes: Math.max(0, Number(formState.cooldownMinutes) || 0),
  });

  const openCreateAlertRuleDialog = () => {
    setEditingAlertRuleId(null);
    setAlertRuleForm({ ...DEFAULT_ALERT_RULE_FORM });
    setAlertRuleDialogOpen(true);
  };

  const openEditAlertRuleDialog = (rule) => {
    setEditingAlertRuleId(rule.id);
    setAlertRuleForm({
      name: rule.name || '',
      enabled: Boolean(rule.enabled),
      severity: rule.severity || 'medium',
      metric: rule.metric || 'anomaly_count',
      operator: rule.operator || 'gte',
      threshold: rule.threshold ?? 0,
      windowMinutes: rule.windowMinutes ?? 15,
      activityType: rule.activityType || '',
      userId: rule.userId ?? '',
      computerId: rule.computerId ?? '',
      notifyInApp: rule.notifyInApp ?? true,
      notifyEmail: rule.notifyEmail ?? false,
      cooldownMinutes: rule.cooldownMinutes ?? 10,
    });
    setAlertRuleDialogOpen(true);
  };

  const fetchAlertRules = async ({ silent = false } = {}) => {
    try {
      if (!silent) setAlertRulesLoading(true);
      setAlertRulesError(null);

      const [rulesResult, metadataResult] = await Promise.allSettled([
        alertRulesAPI.getRules(),
        alertRulesAPI.getMetadata(),
      ]);

      if (rulesResult.status === 'fulfilled') {
        setAlertRules(rulesResult.value?.rules || []);
      }
      if (metadataResult.status === 'fulfilled') {
        setAlertRuleMetadata(metadataResult.value || null);
      }

      if (rulesResult.status !== 'fulfilled' && metadataResult.status !== 'fulfilled') {
        throw rulesResult.reason || metadataResult.reason;
      }
    } catch (err) {
      setAlertRulesError(err?.response?.data?.message || err?.message || 'Failed to load alert rules');
    } finally {
      setAlertRulesLoading(false);
    }
  };

  const handleAlertRuleFieldChange = (field, value) => {
    setAlertRuleForm((prev) => ({ ...prev, [field]: value }));
  };

  const handleSaveAlertRule = async () => {
    try {
      setAlertRuleSaving(true);
      setAlertRulesError(null);
      const payload = normalizeAlertRulePayload(alertRuleForm);

      if (!payload.name) {
        setAlertRulesError('Rule name is required');
        return;
      }

      let savedRule;
      if (editingAlertRuleId) {
        savedRule = await alertRulesAPI.updateRule(editingAlertRuleId, payload);
        setAlertRules((prev) => prev.map((rule) => (rule.id === savedRule.id ? savedRule : rule)));
      } else {
        savedRule = await alertRulesAPI.createRule(payload);
        setAlertRules((prev) => [savedRule, ...prev]);
      }

      setAlertRuleDialogOpen(false);
      setEditingAlertRuleId(null);
      setAlertRuleForm({ ...DEFAULT_ALERT_RULE_FORM });
      setSuccess(`Alert rule ${editingAlertRuleId ? 'updated' : 'created'} successfully`);
      setTimeout(() => setSuccess(null), 3000);
      if (typeof addNotification === 'function') {
        addNotification({
          type: 'success',
          title: `Alert rule ${editingAlertRuleId ? 'updated' : 'created'}`,
          message: savedRule.name,
          timestamp: new Date().toISOString(),
        });
      }
    } catch (err) {
      setAlertRulesError(err?.response?.data?.message || err?.message || 'Failed to save alert rule');
    } finally {
      setAlertRuleSaving(false);
    }
  };

  const handleToggleAlertRule = async (rule) => {
    try {
      const updated = await alertRulesAPI.setEnabled(rule.id, !rule.enabled);
      setAlertRules((prev) => prev.map((item) => (item.id === updated.id ? updated : item)));
    } catch (err) {
      setAlertRulesError(err?.response?.data?.message || err?.message || 'Failed to update rule status');
    }
  };

  const handleDeleteAlertRule = (rule) => {
    setConfirmAction({
      type: 'delete_alert_rule',
      id: rule.id,
      message: `Delete alert rule "${rule.name}"?`,
    });
    setConfirmDialogOpen(true);
  };

  const fetchMonitoringData = async ({ silent = false } = {}) => {
    try {
      if (!silent) {
        setMonitoringDataLoading(true);
      }
      setMonitoringDataError(null);

      const [healthResult, agentsResult] = await Promise.allSettled([
        systemAPI.getHealth(),
        agentAPI.getAgents({ page: 1, pageSize: 100 }),
      ]);

      if (healthResult.status === 'fulfilled') {
        setSystemHealth(healthResult.value);
      }

      if (agentsResult.status === 'fulfilled') {
        setMonitoringAgents(agentsResult.value?.agents || []);
      }

      if (healthResult.status !== 'fulfilled' && agentsResult.status !== 'fulfilled') {
        throw healthResult.reason || agentsResult.reason;
      }

      setMonitoringLastUpdated(new Date());
    } catch (err) {
      setMonitoringDataError(err?.response?.data?.message || err?.message || 'Failed to load monitoring data');
    } finally {
      setMonitoringDataLoading(false);
    }
  };

  const fetchAgentControlData = async (agentId, { silent = false } = {}) => {
    if (!agentId) return;

    try {
      if (!silent) setAgentControlLoading(true);
      setAgentControlError(null);

      const commandQuery = { page: 1, pageSize: 20 };
      if (agentCommandStatusFilter) commandQuery.status = agentCommandStatusFilter;

      const [policyResult, commandsResult] = await Promise.allSettled([
        agentAPI.getAgentPolicy(agentId),
        agentAPI.getAgentCommands(agentId, commandQuery),
      ]);

      if (policyResult.status === 'fulfilled') {
        const policy = policyResult.value || null;
        setSelectedAgentPolicy(policy);
        setAgentPolicyForm(mapAgentPolicyToForm(policy));
      }

      if (commandsResult.status === 'fulfilled') {
        setAgentCommands(commandsResult.value?.commands || []);
        setAgentCommandsTotal(commandsResult.value?.totalCount || 0);
      }

      if (policyResult.status !== 'fulfilled' && commandsResult.status !== 'fulfilled') {
        throw policyResult.reason || commandsResult.reason;
      }
    } catch (err) {
      setAgentControlError(err?.response?.data?.message || err?.message || 'Failed to load agent control data');
    } finally {
      setAgentControlLoading(false);
    }
  };

  const handleAgentPolicyFieldChange = (field, value) => {
    setAgentPolicyForm((prev) => ({ ...prev, [field]: value }));
  };

  const handleSaveAgentPolicy = async () => {
    if (!selectedMonitoringAgentId) return;

    try {
      setAgentPolicySaving(true);
      setAgentControlError(null);

      const payload = buildAgentPolicyPayload(agentPolicyForm);
      const savedPolicy = await agentAPI.upsertAgentPolicy(selectedMonitoringAgentId, payload);

      setSelectedAgentPolicy(savedPolicy);
      setAgentPolicyForm(mapAgentPolicyToForm(savedPolicy));
      setSuccess('Agent policy saved successfully');
      setTimeout(() => setSuccess(null), 3000);
    } catch (err) {
      setAgentControlError(err?.response?.data?.message || err?.message || 'Failed to save agent policy');
    } finally {
      setAgentPolicySaving(false);
    }
  };

  const handleResetAgentPolicy = () => {
    if (!selectedMonitoringAgentId) return;

    setConfirmAction({
      type: 'reset_agent_policy',
      agentId: selectedMonitoringAgentId,
      message: `Reset policy for agent #${selectedMonitoringAgentId} to defaults?`,
    });
    setConfirmDialogOpen(true);
  };

  const handleAgentCommandFieldChange = (field, value) => {
    setAgentCommandForm((prev) => ({ ...prev, [field]: value }));
  };

  const handleCreateAgentCommand = async () => {
    if (!selectedMonitoringAgentId) return;

    try {
      setAgentCommandSaving(true);
      setAgentControlError(null);

      const type = String(agentCommandForm.type || '').trim();
      if (!type) {
        setAgentControlError('Command type is required');
        return;
      }

      const payloadJson = String(agentCommandForm.payloadJson || '').trim() || '{}';
      try {
        JSON.parse(payloadJson);
      } catch {
        setAgentControlError('Command payload must be valid JSON');
        return;
      }

      await agentAPI.createAgentCommand(selectedMonitoringAgentId, {
        type,
        payloadJson,
        requestedBy: agentCommandForm.requestedBy || undefined,
      });

      setSuccess('Agent command queued successfully');
      setTimeout(() => setSuccess(null), 3000);
      await fetchAgentControlData(selectedMonitoringAgentId, { silent: true });
    } catch (err) {
      setAgentControlError(err?.response?.data?.message || err?.message || 'Failed to queue command');
    } finally {
      setAgentCommandSaving(false);
    }
  };

  const handleAgentBlockAction = async (blocked) => {
    if (!selectedMonitoringAgentId) return;

    try {
      setAgentActionLoading(true);
      setAgentControlError(null);

      const reason = String(agentAdminReason || '').trim()
        || (blocked ? 'Blocked by admin' : 'Unblocked by admin');

      if (blocked) {
        await agentAPI.blockWorkstation(selectedMonitoringAgentId, reason);
      } else {
        await agentAPI.unblockWorkstation(selectedMonitoringAgentId, reason);
      }

      setSuccess(blocked ? 'Block command queued' : 'Unblock command queued');
      setTimeout(() => setSuccess(null), 3000);

      await Promise.all([
        fetchAgentControlData(selectedMonitoringAgentId, { silent: true }),
        fetchMonitoringData({ silent: true }),
      ]);
    } catch (err) {
      setAgentControlError(err?.response?.data?.message || err?.message || 'Failed to send block/unblock command');
    } finally {
      setAgentActionLoading(false);
    }
  };

  const healthServices = systemHealth?.services || [];
  const healthyServicesCount = healthServices.filter((service) => service.status === 'healthy').length;
  const selectedMonitoringAgent = monitoringAgents.find((agent) => agent.id === selectedMonitoringAgentId) || null;
  const agentStatusSummary = monitoringAgents.reduce((acc, agent) => {
    const status = (agent?.status || 'unknown').toLowerCase();
    acc[status] = (acc[status] || 0) + 1;
    return acc;
  }, {});
  const alertRuleMetrics = alertRuleMetadata?.metrics || Object.entries(ALERT_RULE_LABELS).map(([key, label]) => ({ key, label }));
  const alertRuleOperators = alertRuleMetadata?.operators || Object.entries(OPERATOR_LABELS).map(([key, label]) => ({ key, label }));
  const alertRuleSeverities = alertRuleMetadata?.severities || ['low', 'medium', 'high', 'critical'];

  return (
    <Box>
      <Box display="flex" justifyContent="space-between" alignItems="center" mb={3}>
        <Typography variant="h4">System Settings</Typography>
        <Button
          variant="outlined"
          startIcon={<Refresh />}
          onClick={handleReloadSettings}
          disabled={loading}
        >
          Reload Settings
        </Button>
      </Box>

      {error && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {error}
        </Alert>
      )}

      {success && (
        <Alert severity="success" sx={{ mb: 2 }}>
          {success}
        </Alert>
      )}

      <Tabs value={tabValue} onChange={handleTabChange} sx={{ mb: 3 }}>
        <Tab label="General" icon={<SettingsIcon />} />
        <Tab label="Security" icon={<Security />} />
        <Tab label="Notifications" icon={<Notifications />} />
        <Tab label="Monitoring" icon={<NetworkCheck />} />
        <Tab label="Whitelist/Blacklist" icon={<Storage />} />
      </Tabs>

      {/* General Settings */}
      {tabValue === 0 && (
        <Card>
          <CardContent>
            <Typography variant="h6" gutterBottom>
              General Configuration
            </Typography>
            <Grid container spacing={3}>
              <Grid item xs={12} md={6}>
                <TextField
                  fullWidth
                  label="System Name"
                  value={generalSettings.systemName}
                  onChange={(e) => setGeneralSettings({ ...generalSettings, systemName: e.target.value })}
                />
              </Grid>
              <Grid item xs={12} md={6}>
                <FormControl fullWidth>
                  <InputLabel>Log Level</InputLabel>
                  <Select
                    value={generalSettings.logLevel}
                    label="Log Level"
                    onChange={(e) => setGeneralSettings({ ...generalSettings, logLevel: e.target.value })}
                  >
                    <MenuItem value="Debug">Debug</MenuItem>
                    <MenuItem value="Info">Info</MenuItem>
                    <MenuItem value="Warning">Warning</MenuItem>
                    <MenuItem value="Error">Error</MenuItem>
                  </Select>
                </FormControl>
              </Grid>
              <Grid item xs={12} md={6}>
                <TextField
                  fullWidth
                  label="Max Log Retention (days)"
                  type="number"
                  value={generalSettings.maxLogRetention}
                  onChange={(e) => setGeneralSettings({ ...generalSettings, maxLogRetention: e.target.value })}
                />
              </Grid>
              <Grid item xs={12} md={6}>
                <TextField
                  fullWidth
                  label="Session Timeout (minutes)"
                  type="number"
                  value={generalSettings.sessionTimeout}
                  onChange={(e) => setGeneralSettings({ ...generalSettings, sessionTimeout: e.target.value })}
                />
              </Grid>
              <Grid item xs={12}>
                <FormControlLabel
                  control={
                    <Switch
                      checked={generalSettings.enableAuditLog}
                      onChange={(e) => setGeneralSettings({ ...generalSettings, enableAuditLog: e.target.checked })}
                    />
                  }
                  label="Enable Audit Logging"
                />
              </Grid>
            </Grid>
            <Box mt={3}>
              <Button
                variant="contained"
                startIcon={<Save />}
                onClick={() => handleSaveSettings('General')}
                disabled={loading}
              >
                Save General Settings
              </Button>
            </Box>
          </CardContent>
        </Card>
      )}

      {/* Security Settings */}
      {tabValue === 1 && (
        <Card>
          <CardContent>
            <Typography variant="h6" gutterBottom>
              Security Configuration
            </Typography>
            <Grid container spacing={3}>
              <Grid item xs={12} md={6}>
                <TextField
                  fullWidth
                  label="Minimum Password Length"
                  type="number"
                  value={securitySettings.passwordMinLength}
                  onChange={(e) => setSecuritySettings({ ...securitySettings, passwordMinLength: e.target.value })}
                />
              </Grid>
              <Grid item xs={12} md={6}>
                <TextField
                  fullWidth
                  label="Max Login Attempts"
                  type="number"
                  value={securitySettings.maxLoginAttempts}
                  onChange={(e) => setSecuritySettings({ ...securitySettings, maxLoginAttempts: e.target.value })}
                />
              </Grid>
              <Grid item xs={12} md={6}>
                <TextField
                  fullWidth
                  label="Lockout Duration (minutes)"
                  type="number"
                  value={securitySettings.lockoutDurationMinutes}
                  onChange={(e) => setSecuritySettings({ ...securitySettings, lockoutDurationMinutes: e.target.value })}
                />
              </Grid>
              <Grid item xs={12} md={6}>
                <TextField
                  fullWidth
                  label="JWT Expiration (hours)"
                  type="number"
                  value={securitySettings.jwtExpirationHours}
                  onChange={(e) => setSecuritySettings({ ...securitySettings, jwtExpirationHours: e.target.value })}
                />
              </Grid>
              <Grid item xs={12} md={6}>
                <FormControlLabel
                  control={
                    <Switch
                      checked={securitySettings.passwordRequireSpecialChars}
                      onChange={(e) => setSecuritySettings({ ...securitySettings, passwordRequireSpecialChars: e.target.checked })}
                    />
                  }
                  label="Require Special Characters in Password"
                />
              </Grid>
              <Grid item xs={12} md={6}>
                <FormControlLabel
                  control={
                    <Switch
                      checked={securitySettings.enableTwoFactor}
                      onChange={(e) => setSecuritySettings({ ...securitySettings, enableTwoFactor: e.target.checked })}
                    />
                  }
                  label="Enable Two-Factor Authentication"
                />
              </Grid>
            </Grid>
            <Box mt={3}>
              <Button
                variant="contained"
                startIcon={<Save />}
                onClick={() => handleSaveSettings('Security')}
                disabled={loading}
              >
                Save Security Settings
              </Button>
            </Box>
          </CardContent>
        </Card>
      )}

      {/* Notification Settings */}
      {tabValue === 2 && (
        <Card>
          <CardContent>
            <Typography variant="h6" gutterBottom>
              Notification Configuration
            </Typography>
            <Grid container spacing={3}>
              <Grid item xs={12} md={6}>
                <TextField
                  fullWidth
                  label="Notification Email"
                  type="email"
                  value={notificationSettings.notificationEmail}
                  onChange={(e) => setNotificationSettings({ ...notificationSettings, notificationEmail: e.target.value })}
                />
              </Grid>
              <Grid item xs={12} md={6}>
                <TextField
                  fullWidth
                  label="SMTP Server"
                  value={notificationSettings.smtpServer}
                  onChange={(e) => setNotificationSettings({ ...notificationSettings, smtpServer: e.target.value })}
                />
              </Grid>
              <Grid item xs={12} md={6}>
                <TextField
                  fullWidth
                  label="SMTP Port"
                  type="number"
                  value={notificationSettings.smtpPort}
                  onChange={(e) => setNotificationSettings({ ...notificationSettings, smtpPort: e.target.value })}
                />
              </Grid>
              <Grid item xs={12} md={6}>
                <TextField
                  fullWidth
                  label="Alert Threshold"
                  type="number"
                  value={notificationSettings.alertThreshold}
                  onChange={(e) => setNotificationSettings({ ...notificationSettings, alertThreshold: e.target.value })}
                />
              </Grid>
              <Grid item xs={12} md={4}>
                <FormControlLabel
                  control={
                    <Switch
                      checked={notificationSettings.emailNotifications}
                      onChange={(e) => setNotificationSettings({ ...notificationSettings, emailNotifications: e.target.checked })}
                    />
                  }
                  label="Email Notifications"
                />
              </Grid>
              <Grid item xs={12} md={4}>
                <FormControlLabel
                  control={
                    <Switch
                      checked={notificationSettings.smsNotifications}
                      onChange={(e) => setNotificationSettings({ ...notificationSettings, smsNotifications: e.target.checked })}
                    />
                  }
                  label="SMS Notifications"
                />
              </Grid>
              <Grid item xs={12} md={4}>
                <FormControlLabel
                  control={
                    <Switch
                      checked={notificationSettings.pushNotifications}
                      onChange={(e) => setNotificationSettings({ ...notificationSettings, pushNotifications: e.target.checked })}
                    />
                  }
                  label="Push Notifications"
                />
              </Grid>
            </Grid>

            <Divider sx={{ my: 3 }} />

            <Box display="flex" justifyContent="space-between" alignItems="center" flexWrap="wrap" gap={2} mb={2}>
              <Box>
                <Typography variant="h6">Alert Rules</Typography>
                <Typography variant="body2" color="text.secondary">
                  Threshold-based rules for anomalies, blocked activity and risk spikes.
                </Typography>
              </Box>
              <Stack direction="row" spacing={1}>
                <Button
                  variant="outlined"
                  startIcon={<Refresh />}
                  onClick={() => fetchAlertRules()}
                  disabled={alertRulesLoading}
                >
                  Refresh Rules
                </Button>
                <Button variant="contained" startIcon={<Add />} onClick={openCreateAlertRuleDialog}>
                  Add Rule
                </Button>
              </Stack>
            </Box>

            {alertRulesError && (
              <Alert severity="warning" sx={{ mb: 2 }}>
                {alertRulesError}
              </Alert>
            )}

            {alertRulesLoading && <LinearProgress sx={{ mb: 2, borderRadius: 999 }} />}

            <TableContainer component={Paper} variant="outlined" sx={{ mb: 3 }}>
              <Table size="small">
                <TableHead>
                  <TableRow>
                    <TableCell>Rule</TableCell>
                    <TableCell>Condition</TableCell>
                    <TableCell>Scope</TableCell>
                    <TableCell>Channels</TableCell>
                    <TableCell>Status</TableCell>
                    <TableCell align="right">Actions</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {alertRules.length === 0 ? (
                    <TableRow>
                      <TableCell colSpan={6} align="center">
                        No alert rules yet. Create one to enable automated alerting.
                      </TableCell>
                    </TableRow>
                  ) : (
                    alertRules.map((rule) => (
                      <TableRow key={rule.id} hover>
                        <TableCell>
                          <Typography variant="body2" fontWeight={700}>
                            {rule.name}
                          </Typography>
                          <Stack direction="row" spacing={0.5} useFlexGap flexWrap="wrap" sx={{ mt: 0.5 }}>
                            <Chip
                              size="small"
                              label={String(rule.severity || 'medium').toUpperCase()}
                              color={
                                String(rule.severity).toLowerCase() === 'critical' ? 'error'
                                  : String(rule.severity).toLowerCase() === 'high' ? 'warning'
                                    : String(rule.severity).toLowerCase() === 'low' ? 'success'
                                      : 'default'
                              }
                            />
                            <Chip size="small" variant="outlined" label={`${rule.windowMinutes || '-'}m`} />
                            <Chip size="small" variant="outlined" label={`${rule.cooldownMinutes || 0}m cd`} />
                          </Stack>
                        </TableCell>
                        <TableCell>
                          {(ALERT_RULE_LABELS[rule.metric] || rule.metric)} {OPERATOR_LABELS[rule.operator] || rule.operator} {rule.threshold}
                        </TableCell>
                        <TableCell>
                          <Stack direction="row" spacing={0.5} useFlexGap flexWrap="wrap">
                            {rule.activityType && <Chip size="small" variant="outlined" label={`Type:${rule.activityType}`} />}
                            {rule.userId && <Chip size="small" variant="outlined" label={`User:${rule.userId}`} />}
                            {rule.computerId && <Chip size="small" variant="outlined" label={`PC:${rule.computerId}`} />}
                            {!rule.activityType && !rule.userId && !rule.computerId && (
                              <Typography variant="caption" color="text.secondary">Global</Typography>
                            )}
                          </Stack>
                        </TableCell>
                        <TableCell>
                          <Stack direction="row" spacing={0.5} useFlexGap flexWrap="wrap">
                            {rule.notifyInApp && <Chip size="small" label="In-app" />}
                            {rule.notifyEmail && <Chip size="small" label="Email" />}
                            {!rule.notifyInApp && !rule.notifyEmail && <Chip size="small" variant="outlined" label="None" />}
                          </Stack>
                        </TableCell>
                        <TableCell>
                          <FormControlLabel
                            sx={{ m: 0 }}
                            control={
                              <Switch
                                size="small"
                                checked={Boolean(rule.enabled)}
                                onChange={() => handleToggleAlertRule(rule)}
                              />
                            }
                            label={rule.enabled ? 'Enabled' : 'Disabled'}
                          />
                        </TableCell>
                        <TableCell align="right">
                          <Tooltip title="Edit rule">
                            <IconButton size="small" onClick={() => openEditAlertRuleDialog(rule)}>
                              <Edit fontSize="small" />
                            </IconButton>
                          </Tooltip>
                          <Tooltip title="Delete rule">
                            <IconButton size="small" color="error" onClick={() => handleDeleteAlertRule(rule)}>
                              <Delete fontSize="small" />
                            </IconButton>
                          </Tooltip>
                        </TableCell>
                      </TableRow>
                    ))
                  )}
                </TableBody>
              </Table>
            </TableContainer>

            <Box mt={3}>
              <Button
                variant="contained"
                startIcon={<Save />}
                onClick={() => handleSaveSettings('Notification')}
                disabled={loading}
              >
                Save Notification Settings
              </Button>
            </Box>
          </CardContent>
        </Card>
      )}

      {/* Monitoring Settings */}
      {tabValue === 3 && (
        <Grid container spacing={3}>
          <Grid item xs={12}>
            <Card>
              <CardContent>
                <Box display="flex" justifyContent="space-between" alignItems="flex-start" gap={2} flexWrap="wrap" mb={2}>
                  <Box>
                    <Typography variant="h6">Live System Health</Typography>
                    <Typography variant="body2" color="text.secondary">
                      Real-time status from gateway aggregated checks (`/api/system/health`) and AgentService.
                    </Typography>
                    <Typography variant="caption" color="text.secondary">
                      Last updated: {monitoringLastUpdated ? monitoringLastUpdated.toLocaleTimeString() : '-'}
                    </Typography>
                  </Box>
                  <Button
                    variant="outlined"
                    startIcon={<Refresh />}
                    onClick={() => fetchMonitoringData()}
                    disabled={monitoringDataLoading}
                  >
                    {monitoringDataLoading ? 'Refreshing...' : 'Refresh Health'}
                  </Button>
                </Box>

                {monitoringDataLoading && <LinearProgress sx={{ mb: 2, borderRadius: 999 }} />}

                {monitoringDataError && (
                  <Alert severity="warning" sx={{ mb: 2 }}>
                    {monitoringDataError}
                  </Alert>
                )}

                <Grid container spacing={2} sx={{ mb: 2 }}>
                  <Grid item xs={12} sm={6} md={3}>
                    <Paper sx={{ p: 2 }}>
                      <Typography variant="body2" color="text.secondary">Overall Status</Typography>
                      <Chip
                        size="small"
                        color={systemHealth?.status === 'healthy' ? 'success' : systemHealth?.status === 'degraded' ? 'warning' : 'error'}
                        label={(systemHealth?.status || 'unknown').toUpperCase()}
                        sx={{ mt: 1 }}
                      />
                    </Paper>
                  </Grid>
                  <Grid item xs={12} sm={6} md={3}>
                    <Paper sx={{ p: 2 }}>
                      <Typography variant="body2" color="text.secondary">Services Healthy</Typography>
                      <Typography variant="h5">{healthyServicesCount}/{healthServices.length}</Typography>
                    </Paper>
                  </Grid>
                  <Grid item xs={12} sm={6} md={3}>
                    <Paper sx={{ p: 2 }}>
                      <Typography variant="body2" color="text.secondary">Agents Total</Typography>
                      <Typography variant="h5">{monitoringAgents.length}</Typography>
                    </Paper>
                  </Grid>
                  <Grid item xs={12} sm={6} md={3}>
                    <Paper sx={{ p: 2 }}>
                      <Typography variant="body2" color="text.secondary">Agents Online</Typography>
                      <Typography variant="h5">{agentStatusSummary.online || agentStatusSummary.active || 0}</Typography>
                    </Paper>
                  </Grid>
                </Grid>

                <Grid container spacing={2}>
                  <Grid item xs={12} lg={7}>
                    <Typography variant="subtitle1" gutterBottom>Service Checks</Typography>
                    <TableContainer component={Paper} variant="outlined">
                      <Table size="small">
                        <TableHead>
                          <TableRow>
                            <TableCell>Service</TableCell>
                            <TableCell>Status</TableCell>
                            <TableCell align="right">Latency</TableCell>
                            <TableCell align="right">HTTP</TableCell>
                          </TableRow>
                        </TableHead>
                        <TableBody>
                          {healthServices.length === 0 ? (
                            <TableRow>
                              <TableCell colSpan={4} align="center">No health data loaded yet</TableCell>
                            </TableRow>
                          ) : healthServices.map((service) => (
                            <TableRow key={service.name} hover>
                              <TableCell>{service.name}</TableCell>
                              <TableCell>
                                <Chip
                                  size="small"
                                  color={service.status === 'healthy' ? 'success' : service.status === 'degraded' ? 'warning' : 'error'}
                                  label={(service.status || 'unknown').toUpperCase()}
                                />
                              </TableCell>
                              <TableCell align="right">{service.latencyMs ?? '-'} ms</TableCell>
                              <TableCell align="right">{service.httpStatus ?? '-'}</TableCell>
                            </TableRow>
                          ))}
                        </TableBody>
                      </Table>
                    </TableContainer>
                  </Grid>

                  <Grid item xs={12} lg={5}>
                    <Typography variant="subtitle1" gutterBottom>Agents & Heartbeats</Typography>
                    <TableContainer component={Paper} variant="outlined">
                      <Table size="small">
                        <TableHead>
                          <TableRow>
                            <TableCell>ID</TableCell>
                            <TableCell>Status</TableCell>
                            <TableCell>Version</TableCell>
                            <TableCell>Last Heartbeat</TableCell>
                          </TableRow>
                        </TableHead>
                        <TableBody>
                          {monitoringAgents.length === 0 ? (
                            <TableRow>
                              <TableCell colSpan={4} align="center">No agents found</TableCell>
                            </TableRow>
                          ) : monitoringAgents.slice(0, 20).map((agent) => (
                            <TableRow key={agent.id} hover>
                              <TableCell>{agent.id}</TableCell>
                              <TableCell>
                                <Chip
                                  size="small"
                                  color={String(agent.status || '').toLowerCase().includes('online') || String(agent.status || '').toLowerCase().includes('active') ? 'success' : 'warning'}
                                  label={(agent.status || 'unknown').toUpperCase()}
                                />
                              </TableCell>
                              <TableCell>{agent.version || '-'}</TableCell>
                              <TableCell>
                                {agent.lastHeartbeat ? new Date(agent.lastHeartbeat).toLocaleString() : '-'}
                              </TableCell>
                            </TableRow>
                          ))}
                        </TableBody>
                      </Table>
                    </TableContainer>
                  </Grid>
                </Grid>
              </CardContent>
            </Card>
          </Grid>

          <Grid item xs={12}>
            <Card>
              <CardContent>
                <Box display="flex" justifyContent="space-between" alignItems="flex-start" gap={2} flexWrap="wrap" mb={2}>
                  <Box>
                    <Typography variant="h6">Agent Control Plane</Typography>
                    <Typography variant="body2" color="text.secondary">
                      Configure collection policy and send direct commands (block/unblock, refresh policy) for a selected endpoint agent.
                    </Typography>
                  </Box>
                  <Stack direction="row" spacing={1}>
                    <Button
                      variant="outlined"
                      startIcon={<Refresh />}
                      onClick={() => selectedMonitoringAgentId && fetchAgentControlData(selectedMonitoringAgentId)}
                      disabled={!selectedMonitoringAgentId || agentControlLoading}
                    >
                      {agentControlLoading ? 'Refreshing...' : 'Refresh Agent'}
                    </Button>
                  </Stack>
                </Box>

                {agentControlError && (
                  <Alert severity="warning" sx={{ mb: 2 }}>
                    {agentControlError}
                  </Alert>
                )}

                {monitoringAgents.length === 0 ? (
                  <Alert severity="info">
                    No agents available yet. Start a local endpoint agent and wait for heartbeat registration.
                  </Alert>
                ) : (
                  <Grid container spacing={2}>
                    <Grid item xs={12}>
                      <Grid container spacing={2} alignItems="center">
                        <Grid item xs={12} md={4}>
                          <FormControl fullWidth size="small">
                            <InputLabel>Selected Agent</InputLabel>
                            <Select
                              label="Selected Agent"
                              value={selectedMonitoringAgentId || ''}
                              onChange={(e) => setSelectedMonitoringAgentId(Number(e.target.value))}
                            >
                              {monitoringAgents.map((agent) => (
                                <MenuItem key={agent.id} value={agent.id}>
                                  #{agent.id} · PC {agent.computerId ?? '-'} · {(agent.status || 'unknown').toUpperCase()}
                                </MenuItem>
                              ))}
                            </Select>
                          </FormControl>
                        </Grid>

                        <Grid item xs={12} md={8}>
                          <Stack direction="row" spacing={1} useFlexGap flexWrap="wrap" alignItems="center">
                            <Chip
                              size="small"
                              label={`Agent #${selectedMonitoringAgent?.id ?? '-'}`}
                              variant="outlined"
                            />
                            <Chip
                              size="small"
                              label={`Computer: ${selectedMonitoringAgent?.computerId ?? '-'}`}
                              variant="outlined"
                            />
                            <Chip
                              size="small"
                              color={String(selectedMonitoringAgent?.status || '').toLowerCase().includes('online') || String(selectedMonitoringAgent?.status || '').toLowerCase().includes('active') ? 'success' : 'warning'}
                              label={(selectedMonitoringAgent?.status || 'unknown').toUpperCase()}
                            />
                            <Chip
                              size="small"
                              color={agentPolicyForm.adminBlocked ? 'error' : 'success'}
                              label={agentPolicyForm.adminBlocked ? 'ADMIN BLOCKED' : 'NOT BLOCKED'}
                            />
                            {selectedMonitoringAgent?.lastHeartbeat && (
                              <Chip
                                size="small"
                                variant="outlined"
                                label={`Heartbeat: ${new Date(selectedMonitoringAgent.lastHeartbeat).toLocaleTimeString()}`}
                              />
                            )}
                          </Stack>
                        </Grid>
                      </Grid>
                    </Grid>

                    <Grid item xs={12} lg={7}>
                      <Paper variant="outlined" sx={{ p: 2 }}>
                        <Box display="flex" justifyContent="space-between" alignItems="center" mb={2}>
                          <Typography variant="subtitle1">Collection Policy</Typography>
                          <Stack direction="row" spacing={1}>
                            <Button
                              size="small"
                              variant="outlined"
                              color="warning"
                              onClick={handleResetAgentPolicy}
                              disabled={!selectedMonitoringAgentId || agentPolicySaving || agentControlLoading}
                            >
                              Reset Policy
                            </Button>
                            <Button
                              size="small"
                              variant="contained"
                              startIcon={<Save />}
                              onClick={handleSaveAgentPolicy}
                              disabled={!selectedMonitoringAgentId || agentPolicySaving}
                            >
                              {agentPolicySaving ? 'Saving...' : 'Save Policy'}
                            </Button>
                          </Stack>
                        </Box>

                        <Grid container spacing={2}>
                          <Grid item xs={12} md={4}>
                            <TextField
                              fullWidth
                              size="small"
                              type="number"
                              label="Collection Interval (s)"
                              value={agentPolicyForm.collectionIntervalSec}
                              onChange={(e) => handleAgentPolicyFieldChange('collectionIntervalSec', e.target.value)}
                            />
                          </Grid>
                          <Grid item xs={12} md={4}>
                            <TextField
                              fullWidth
                              size="small"
                              type="number"
                              label="Heartbeat Interval (s)"
                              value={agentPolicyForm.heartbeatIntervalSec}
                              onChange={(e) => handleAgentPolicyFieldChange('heartbeatIntervalSec', e.target.value)}
                            />
                          </Grid>
                          <Grid item xs={12} md={4}>
                            <TextField
                              fullWidth
                              size="small"
                              type="number"
                              label="Flush Interval (s)"
                              value={agentPolicyForm.flushIntervalSec}
                              onChange={(e) => handleAgentPolicyFieldChange('flushIntervalSec', e.target.value)}
                            />
                          </Grid>

                          <Grid item xs={12} md={4}>
                            <TextField
                              fullWidth
                              size="small"
                              type="number"
                              label="Idle Threshold (s)"
                              value={agentPolicyForm.idleThresholdSec}
                              onChange={(e) => handleAgentPolicyFieldChange('idleThresholdSec', e.target.value)}
                            />
                          </Grid>
                          <Grid item xs={12} md={4}>
                            <TextField
                              fullWidth
                              size="small"
                              type="number"
                              label="Browser Poll (s)"
                              value={agentPolicyForm.browserPollIntervalSec}
                              onChange={(e) => handleAgentPolicyFieldChange('browserPollIntervalSec', e.target.value)}
                            />
                          </Grid>
                          <Grid item xs={12} md={4}>
                            <TextField
                              fullWidth
                              size="small"
                              type="number"
                              label="Process Snapshot Limit"
                              value={agentPolicyForm.processSnapshotLimit}
                              onChange={(e) => handleAgentPolicyFieldChange('processSnapshotLimit', e.target.value)}
                            />
                          </Grid>

                          <Grid item xs={12} md={6}>
                            <TextField
                              fullWidth
                              size="small"
                              type="number"
                              inputProps={{ min: 0, max: 100, step: 0.1 }}
                              label="High Risk Threshold"
                              value={agentPolicyForm.highRiskThreshold}
                              onChange={(e) => handleAgentPolicyFieldChange('highRiskThreshold', e.target.value)}
                            />
                          </Grid>
                          <Grid item xs={12} md={6}>
                            <TextField
                              fullWidth
                              size="small"
                              label="Browsers (comma-separated)"
                              value={agentPolicyForm.browsersCsv}
                              onChange={(e) => handleAgentPolicyFieldChange('browsersCsv', e.target.value)}
                              placeholder="chrome, edge, firefox"
                            />
                          </Grid>

                          <Grid item xs={12}>
                            <TextField
                              fullWidth
                              size="small"
                              label="Blocked Reason"
                              value={agentPolicyForm.blockedReason}
                              onChange={(e) => handleAgentPolicyFieldChange('blockedReason', e.target.value)}
                              helperText="Used when admin block is active; also set automatically by block/unblock actions."
                            />
                          </Grid>

                          <Grid item xs={12} md={6}>
                            <FormControlLabel
                              control={
                                <Switch
                                  checked={Boolean(agentPolicyForm.enableProcessCollection)}
                                  onChange={(e) => handleAgentPolicyFieldChange('enableProcessCollection', e.target.checked)}
                                />
                              }
                              label="Collect Processes"
                            />
                          </Grid>
                          <Grid item xs={12} md={6}>
                            <FormControlLabel
                              control={
                                <Switch
                                  checked={Boolean(agentPolicyForm.enableBrowserCollection)}
                                  onChange={(e) => handleAgentPolicyFieldChange('enableBrowserCollection', e.target.checked)}
                                />
                              }
                              label="Collect Browser Visits"
                            />
                          </Grid>
                          <Grid item xs={12} md={6}>
                            <FormControlLabel
                              control={
                                <Switch
                                  checked={Boolean(agentPolicyForm.enableActiveWindowCollection)}
                                  onChange={(e) => handleAgentPolicyFieldChange('enableActiveWindowCollection', e.target.checked)}
                                />
                              }
                              label="Collect Active Window"
                            />
                          </Grid>
                          <Grid item xs={12} md={6}>
                            <FormControlLabel
                              control={
                                <Switch
                                  checked={Boolean(agentPolicyForm.enableIdleCollection)}
                                  onChange={(e) => handleAgentPolicyFieldChange('enableIdleCollection', e.target.checked)}
                                />
                              }
                              label="Collect Idle Time"
                            />
                          </Grid>
                          <Grid item xs={12} md={6}>
                            <FormControlLabel
                              control={
                                <Switch
                                  checked={Boolean(agentPolicyForm.autoLockEnabled)}
                                  onChange={(e) => handleAgentPolicyFieldChange('autoLockEnabled', e.target.checked)}
                                />
                              }
                              label="Auto-lock on High Risk"
                            />
                          </Grid>
                          <Grid item xs={12} md={6}>
                            <FormControlLabel
                              control={
                                <Switch
                                  checked={Boolean(agentPolicyForm.adminBlocked)}
                                  disabled
                                />
                              }
                              label="Admin Block Flag (read-only)"
                            />
                          </Grid>
                        </Grid>

                        <Divider sx={{ my: 2 }} />

                        <Box display="flex" justifyContent="space-between" alignItems="center" gap={2} flexWrap="wrap">
                          <Typography variant="caption" color="text.secondary">
                            Policy version: {selectedAgentPolicy?.policyVersion || '-'} | Updated:{' '}
                            {selectedAgentPolicy?.updatedAt ? new Date(selectedAgentPolicy.updatedAt).toLocaleString() : '-'}
                          </Typography>
                          <Stack direction="row" spacing={1}>
                            <Button
                              size="small"
                              color="error"
                              variant="outlined"
                              onClick={() => handleAgentBlockAction(true)}
                              disabled={!selectedMonitoringAgentId || agentActionLoading}
                            >
                              Block PC
                            </Button>
                            <Button
                              size="small"
                              color="success"
                              variant="outlined"
                              onClick={() => handleAgentBlockAction(false)}
                              disabled={!selectedMonitoringAgentId || agentActionLoading}
                            >
                              Unblock PC
                            </Button>
                          </Stack>
                        </Box>
                      </Paper>
                    </Grid>

                    <Grid item xs={12} lg={5}>
                      <Paper variant="outlined" sx={{ p: 2, mb: 2 }}>
                        <Typography variant="subtitle1" gutterBottom>Admin Commands</Typography>
                        <Grid container spacing={2}>
                          <Grid item xs={12} sm={6}>
                            <FormControl fullWidth size="small">
                              <InputLabel>Command Type</InputLabel>
                              <Select
                                label="Command Type"
                                value={agentCommandForm.type}
                                onChange={(e) => handleAgentCommandFieldChange('type', e.target.value)}
                              >
                                {AGENT_COMMAND_TYPES.map((type) => (
                                  <MenuItem key={type} value={type}>{type}</MenuItem>
                                ))}
                              </Select>
                            </FormControl>
                          </Grid>
                          <Grid item xs={12} sm={6}>
                            <TextField
                              fullWidth
                              size="small"
                              label="Requested By (optional)"
                              value={agentCommandForm.requestedBy}
                              onChange={(e) => handleAgentCommandFieldChange('requestedBy', e.target.value)}
                              placeholder="admin"
                            />
                          </Grid>
                          <Grid item xs={12}>
                            <TextField
                              fullWidth
                              size="small"
                              label="Reason / Note"
                              value={agentAdminReason}
                              onChange={(e) => setAgentAdminReason(e.target.value)}
                              placeholder="Optional reason for block/unblock"
                            />
                          </Grid>
                          <Grid item xs={12}>
                            <TextField
                              fullWidth
                              size="small"
                              multiline
                              minRows={4}
                              label="Command Payload JSON"
                              value={agentCommandForm.payloadJson}
                              onChange={(e) => handleAgentCommandFieldChange('payloadJson', e.target.value)}
                              placeholder='{"reason":"Manual action"}'
                            />
                          </Grid>
                          <Grid item xs={12}>
                            <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
                              <Button
                                variant="contained"
                                onClick={handleCreateAgentCommand}
                                disabled={!selectedMonitoringAgentId || agentCommandSaving}
                              >
                                {agentCommandSaving ? 'Sending...' : 'Send Command'}
                              </Button>
                              <Button
                                variant="outlined"
                                onClick={() => handleAgentCommandFieldChange('payloadJson', '{}')}
                                disabled={agentCommandSaving}
                              >
                                Reset Payload
                              </Button>
                            </Stack>
                          </Grid>
                        </Grid>
                      </Paper>

                      <Paper variant="outlined" sx={{ p: 2 }}>
                        <Box display="flex" justifyContent="space-between" alignItems="center" gap={2} flexWrap="wrap" mb={1.5}>
                          <Typography variant="subtitle1">
                            Command History ({agentCommandsTotal || agentCommands.length})
                          </Typography>
                          <Stack direction="row" spacing={1}>
                            <FormControl size="small" sx={{ minWidth: 140 }}>
                              <InputLabel>Status</InputLabel>
                              <Select
                                label="Status"
                                value={agentCommandStatusFilter}
                                onChange={(e) => setAgentCommandStatusFilter(e.target.value)}
                              >
                                {AGENT_COMMAND_STATUS_OPTIONS.map((option) => (
                                  <MenuItem key={option.value || 'all'} value={option.value}>
                                    {option.label}
                                  </MenuItem>
                                ))}
                              </Select>
                            </FormControl>
                            <Button
                              size="small"
                              variant="outlined"
                              startIcon={<Refresh />}
                              onClick={() => selectedMonitoringAgentId && fetchAgentControlData(selectedMonitoringAgentId)}
                              disabled={!selectedMonitoringAgentId || agentControlLoading}
                            >
                              Refresh
                            </Button>
                          </Stack>
                        </Box>

                        <TableContainer component={Paper} variant="outlined">
                          <Table size="small">
                            <TableHead>
                              <TableRow>
                                <TableCell>ID</TableCell>
                                <TableCell>Type</TableCell>
                                <TableCell>Status</TableCell>
                                <TableCell>Created</TableCell>
                              </TableRow>
                            </TableHead>
                            <TableBody>
                              {agentCommands.length === 0 ? (
                                <TableRow>
                                  <TableCell colSpan={4} align="center">No commands found</TableCell>
                                </TableRow>
                              ) : agentCommands.map((command) => (
                                <TableRow key={command.id} hover>
                                  <TableCell>{command.id}</TableCell>
                                  <TableCell>
                                    <Typography variant="body2" fontWeight={600}>
                                      {command.type}
                                    </Typography>
                                    {command.resultMessage && (
                                      <Typography variant="caption" color="text.secondary">
                                        {command.resultMessage}
                                      </Typography>
                                    )}
                                  </TableCell>
                                  <TableCell>
                                    <Chip
                                      size="small"
                                      color={getCommandStatusColor(command.status)}
                                      label={String(command.status || 'unknown').toUpperCase()}
                                    />
                                  </TableCell>
                                  <TableCell>
                                    <Typography variant="caption" display="block">
                                      {command.createdAt ? new Date(command.createdAt).toLocaleString() : '-'}
                                    </Typography>
                                    <Typography variant="caption" color="text.secondary" display="block">
                                      Ack: {command.acknowledgedAt ? new Date(command.acknowledgedAt).toLocaleString() : '-'}
                                    </Typography>
                                  </TableCell>
                                </TableRow>
                              ))}
                            </TableBody>
                          </Table>
                        </TableContainer>
                      </Paper>
                    </Grid>
                  </Grid>
                )}
              </CardContent>
            </Card>
          </Grid>

          <Grid item xs={12}>
            <Card>
              <CardContent>
                <Typography variant="h6" gutterBottom>
                  Monitoring Configuration
                </Typography>
                <Grid container spacing={3}>
                  <Grid item xs={12} md={6}>
                    <TextField
                      fullWidth
                      label="Data Retention (days)"
                      type="number"
                      value={monitoringSettings.dataRetentionDays}
                      onChange={(e) => setMonitoringSettings({ ...monitoringSettings, dataRetentionDays: e.target.value })}
                    />
                  </Grid>
                  <Grid item xs={12} md={6}>
                    <TextField
                      fullWidth
                      label="Monitoring Interval (seconds)"
                      type="number"
                      value={monitoringSettings.monitoringInterval}
                      onChange={(e) => setMonitoringSettings({ ...monitoringSettings, monitoringInterval: e.target.value })}
                    />
                  </Grid>
                  <Grid item xs={12} md={4}>
                    <FormControlLabel
                      control={
                        <Switch
                          checked={monitoringSettings.realTimeMonitoring}
                          onChange={(e) => setMonitoringSettings({ ...monitoringSettings, realTimeMonitoring: e.target.checked })}
                        />
                      }
                      label="Real-time Monitoring"
                    />
                  </Grid>
                  <Grid item xs={12} md={4}>
                    <FormControlLabel
                      control={
                        <Switch
                          checked={monitoringSettings.anomalyDetection}
                          onChange={(e) => setMonitoringSettings({ ...monitoringSettings, anomalyDetection: e.target.checked })}
                        />
                      }
                      label="Anomaly Detection"
                    />
                  </Grid>
                  <Grid item xs={12} md={4}>
                    <FormControlLabel
                      control={
                        <Switch
                          checked={monitoringSettings.enableWhitelist}
                          onChange={(e) => setMonitoringSettings({ ...monitoringSettings, enableWhitelist: e.target.checked })}
                        />
                      }
                      label="Enable Whitelist"
                    />
                  </Grid>
                </Grid>
                <Box mt={3}>
                  <Button
                    variant="contained"
                    startIcon={<Save />}
                    onClick={() => handleSaveSettings('Monitoring')}
                    disabled={loading}
                  >
                    Save Monitoring Settings
                  </Button>
                </Box>
              </CardContent>
            </Card>
          </Grid>
        </Grid>
      )}

      {/* Whitelist/Blacklist Settings */}
      {tabValue === 4 && (
        <Grid container spacing={3}>
          <Grid item xs={12} md={6}>
            <Card>
              <CardContent>
                <Box display="flex" justifyContent="space-between" alignItems="center" mb={2}>
                  <Typography variant="h6">Whitelist</Typography>
                  <Button
                    variant="outlined"
                    size="small"
                    startIcon={<Add />}
                    onClick={handleAddWhitelistEntry}
                  >
                    Add Entry
                  </Button>
                </Box>
                <List>
                  {whitelistEntries.map((entry) => (
                    <ListItem key={entry.id} divider>
                      <ListItemText
                        primary={
                          <TextField
                            fullWidth
                            size="small"
                            placeholder="Application name"
                            value={entry.application}
                            onChange={(e) => handleUpdateWhitelistEntry(entry.id, 'application', e.target.value)}
                          />
                        }
                        secondary={
                          <TextField
                            fullWidth
                            size="small"
                            placeholder="Description"
                            value={entry.description}
                            onChange={(e) => handleUpdateWhitelistEntry(entry.id, 'description', e.target.value)}
                            sx={{ mt: 1 }}
                          />
                        }
                      />
                      <ListItemSecondaryAction>
                        <IconButton
                          edge="end"
                          onClick={() => handleDeleteWhitelistEntry(entry.id)}
                          color="error"
                        >
                          <Delete />
                        </IconButton>
                      </ListItemSecondaryAction>
                    </ListItem>
                  ))}
                </List>
              </CardContent>
            </Card>
          </Grid>
          
          <Grid item xs={12} md={6}>
            <Card>
              <CardContent>
                <Box display="flex" justifyContent="space-between" alignItems="center" mb={2}>
                  <Typography variant="h6">Blacklist</Typography>
                  <Button
                    variant="outlined"
                    size="small"
                    startIcon={<Add />}
                    onClick={handleAddBlacklistEntry}
                  >
                    Add Entry
                  </Button>
                </Box>
                <List>
                  {blacklistEntries.map((entry) => (
                    <ListItem key={entry.id} divider>
                      <ListItemText
                        primary={
                          <TextField
                            fullWidth
                            size="small"
                            placeholder="Application name"
                            value={entry.application}
                            onChange={(e) => handleUpdateBlacklistEntry(entry.id, 'application', e.target.value)}
                          />
                        }
                        secondary={
                          <TextField
                            fullWidth
                            size="small"
                            placeholder="Description"
                            value={entry.description}
                            onChange={(e) => handleUpdateBlacklistEntry(entry.id, 'description', e.target.value)}
                            sx={{ mt: 1 }}
                          />
                        }
                      />
                      <ListItemSecondaryAction>
                        <IconButton
                          edge="end"
                          onClick={() => handleDeleteBlacklistEntry(entry.id)}
                          color="error"
                        >
                          <Delete />
                        </IconButton>
                      </ListItemSecondaryAction>
                    </ListItem>
                  ))}
                </List>
              </CardContent>
            </Card>
          </Grid>
        </Grid>
      )}

      <Dialog
        open={alertRuleDialogOpen}
        onClose={() => setAlertRuleDialogOpen(false)}
        fullWidth
        maxWidth="md"
      >
        <DialogTitle>{editingAlertRuleId ? 'Edit Alert Rule' : 'Create Alert Rule'}</DialogTitle>
        <DialogContent>
          <Grid container spacing={2} sx={{ mt: 0.5 }}>
            <Grid item xs={12} md={8}>
              <TextField
                fullWidth
                label="Rule Name"
                value={alertRuleForm.name}
                onChange={(e) => handleAlertRuleFieldChange('name', e.target.value)}
              />
            </Grid>
            <Grid item xs={12} md={4}>
              <FormControlLabel
                control={
                  <Switch
                    checked={Boolean(alertRuleForm.enabled)}
                    onChange={(e) => handleAlertRuleFieldChange('enabled', e.target.checked)}
                  />
                }
                label="Enabled"
              />
            </Grid>
            <Grid item xs={12} md={4}>
              <FormControl fullWidth>
                <InputLabel>Metric</InputLabel>
                <Select
                  label="Metric"
                  value={alertRuleForm.metric}
                  onChange={(e) => handleAlertRuleFieldChange('metric', e.target.value)}
                >
                  {alertRuleMetrics.map((metric) => (
                    <MenuItem key={metric.key} value={metric.key}>
                      {metric.label}
                    </MenuItem>
                  ))}
                </Select>
              </FormControl>
            </Grid>
            <Grid item xs={12} md={4}>
              <FormControl fullWidth>
                <InputLabel>Operator</InputLabel>
                <Select
                  label="Operator"
                  value={alertRuleForm.operator}
                  onChange={(e) => handleAlertRuleFieldChange('operator', e.target.value)}
                >
                  {alertRuleOperators.map((operator) => (
                    <MenuItem key={operator.key} value={operator.key}>
                      {operator.label}
                    </MenuItem>
                  ))}
                </Select>
              </FormControl>
            </Grid>
            <Grid item xs={12} md={4}>
              <TextField
                fullWidth
                label="Threshold"
                type="number"
                value={alertRuleForm.threshold}
                onChange={(e) => handleAlertRuleFieldChange('threshold', e.target.value)}
              />
            </Grid>
            <Grid item xs={12} md={4}>
              <FormControl fullWidth>
                <InputLabel>Severity</InputLabel>
                <Select
                  label="Severity"
                  value={alertRuleForm.severity}
                  onChange={(e) => handleAlertRuleFieldChange('severity', e.target.value)}
                >
                  {alertRuleSeverities.map((severity) => (
                    <MenuItem key={severity} value={severity}>
                      {String(severity).charAt(0).toUpperCase() + String(severity).slice(1)}
                    </MenuItem>
                  ))}
                </Select>
              </FormControl>
            </Grid>
            <Grid item xs={12} md={4}>
              <TextField
                fullWidth
                label="Window (minutes)"
                type="number"
                value={alertRuleForm.windowMinutes}
                onChange={(e) => handleAlertRuleFieldChange('windowMinutes', e.target.value)}
              />
            </Grid>
            <Grid item xs={12} md={4}>
              <TextField
                fullWidth
                label="Cooldown (minutes)"
                type="number"
                value={alertRuleForm.cooldownMinutes}
                onChange={(e) => handleAlertRuleFieldChange('cooldownMinutes', e.target.value)}
              />
            </Grid>
            <Grid item xs={12} md={4}>
              <TextField
                fullWidth
                label="Activity Type (optional)"
                placeholder="FILE_ACCESS"
                value={alertRuleForm.activityType}
                onChange={(e) => handleAlertRuleFieldChange('activityType', e.target.value)}
              />
            </Grid>
            <Grid item xs={12} md={4}>
              <TextField
                fullWidth
                label="User ID (optional)"
                type="number"
                value={alertRuleForm.userId}
                onChange={(e) => handleAlertRuleFieldChange('userId', e.target.value)}
              />
            </Grid>
            <Grid item xs={12} md={6}>
              <TextField
                fullWidth
                label="Computer ID (optional)"
                type="number"
                value={alertRuleForm.computerId}
                onChange={(e) => handleAlertRuleFieldChange('computerId', e.target.value)}
              />
            </Grid>
            <Grid item xs={12} md={3}>
              <FormControlLabel
                control={
                  <Switch
                    checked={Boolean(alertRuleForm.notifyInApp)}
                    onChange={(e) => handleAlertRuleFieldChange('notifyInApp', e.target.checked)}
                  />
                }
                label="In-app"
              />
            </Grid>
            <Grid item xs={12} md={3}>
              <FormControlLabel
                control={
                  <Switch
                    checked={Boolean(alertRuleForm.notifyEmail)}
                    onChange={(e) => handleAlertRuleFieldChange('notifyEmail', e.target.checked)}
                  />
                }
                label="Email"
              />
            </Grid>
          </Grid>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setAlertRuleDialogOpen(false)} disabled={alertRuleSaving}>
            Cancel
          </Button>
          <Button
            onClick={handleSaveAlertRule}
            variant="contained"
            startIcon={<Save />}
            disabled={alertRuleSaving}
          >
            {alertRuleSaving ? 'Saving...' : editingAlertRuleId ? 'Save Rule' : 'Create Rule'}
          </Button>
        </DialogActions>
      </Dialog>

      {/* Confirmation Dialog */}
      <Dialog open={confirmDialogOpen} onClose={() => setConfirmDialogOpen(false)}>
        <DialogTitle>Confirm Action</DialogTitle>
        <DialogContent>
          <Typography>{confirmAction?.message}</Typography>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setConfirmDialogOpen(false)}>Cancel</Button>
          <Button onClick={handleConfirmAction} variant="contained" color="primary">
            Confirm
          </Button>
        </DialogActions>
      </Dialog>
    </Box>
  );
};

export default Settings;
