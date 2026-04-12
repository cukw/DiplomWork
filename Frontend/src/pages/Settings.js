import React, { useState, useEffect, useRef, useCallback } from 'react';
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
  Refresh,
  Replay,
  FileDownload
} from '@mui/icons-material';
import { useAuth } from '../contexts/AuthContext';
import { useNotifications } from '../contexts/NotificationContext';
import { agentAPI, systemAPI, alertRulesAPI, settingsAPI, auditAPI, rbacAPI } from '../services/api';
import { buildAuditCsv, formatAuditDetailsPreview } from '../utils/auditTransforms';
import { mapRolePermissionsToRows, mapRowsToRolePermissions } from '../utils/rbacTransforms';

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
  anomaly_count: 'Количество аномалий',
  blocked_activities: 'Заблокированные действия',
  average_risk_score: 'Средний риск',
  total_activities: 'Всего активностей',
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

const ACCESS_LIST_AUTOSAVE_STORAGE_KEY = 'settings.accessLists.autosave';

const AGENT_COMMAND_TYPES = [
  { value: 'PING', label: 'Проверка связи (PING)' },
  { value: 'REFRESH_POLICY', label: 'Обновить политику (REFRESH_POLICY)' },
  { value: 'BLOCK_WORKSTATION', label: 'Заблокировать ПК (BLOCK_WORKSTATION)' },
  { value: 'UNBLOCK_WORKSTATION', label: 'Разблокировать ПК (UNBLOCK_WORKSTATION)' },
  { value: 'SELF_UPDATE', label: 'Самообновление агента (SELF_UPDATE)' },
  { value: 'SET_COLLECTION_STATE', label: 'Режим сбора (SET_COLLECTION_STATE)' },
  { value: 'SET_LOG_LEVEL', label: 'Уровень логирования (SET_LOG_LEVEL)' },
];

const AGENT_COMMAND_STATUS_OPTIONS = [
  { value: '', label: 'Все' },
  { value: 'pending', label: 'В очереди' },
  { value: 'running', label: 'Выполняется' },
  { value: 'success', label: 'Успешно' },
  { value: 'failed', label: 'Ошибка' },
  { value: 'timeout', label: 'Таймаут' },
  { value: 'deadletter', label: 'В карантине (DLQ)' },
  { value: 'ignored', label: 'Игнорировано' },
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
  if (normalized === 'timeout') return 'warning';
  if (normalized === 'failed' || normalized === 'deadletter') return 'error';
  if (normalized === 'ignored') return 'default';
  return 'default';
};

const getStatusLabel = (status) => {
  const normalized = String(status || '').toLowerCase();
  const labels = {
    healthy: 'Исправен',
    degraded: 'Частично доступен',
    unhealthy: 'Неисправен',
    online: 'Онлайн',
    offline: 'Оффлайн',
    active: 'Активен',
    inactive: 'Неактивен',
    unknown: 'Неизвестно',
    pending: 'В очереди',
    running: 'Выполняется',
    success: 'Успешно',
    failed: 'Ошибка',
    timeout: 'Таймаут',
    deadletter: 'В карантине (DLQ)',
    ignored: 'Игнорировано',
    ready: 'Готово',
  };
  return labels[normalized] || String(status || 'Неизвестно');
};

const getCommandTypeLabel = (type) => {
  return AGENT_COMMAND_TYPES.find((item) => item.value === type)?.label || String(type || '—');
};

const isInFlightCommand = (status) => {
  const normalized = String(status || '').toLowerCase();
  return normalized === 'pending' || normalized === 'running';
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

const normalizeListEntries = (entries) => (Array.isArray(entries) ? entries : [])
  .map((entry, index) => {
    const parsedId = Number(entry?.id);
    const hasNumericId = Number.isFinite(parsedId) && parsedId !== 0;
    return {
      id: hasNumericId ? parsedId : -(index + 1),
      application: String(entry?.application || '').trim(),
      description: String(entry?.description || '').trim(),
    };
  })
  .filter((entry) => entry.application);

const getPolicySyncSeverity = (syncInfo) => {
  const failedAgents = Number(syncInfo?.failedAgents) || 0;
  const syncedAgents = Number(syncInfo?.syncedAgents) || 0;
  if (failedAgents <= 0) return 'success';
  if (syncedAgents > 0) return 'warning';
  return 'error';
};

const getPolicySyncSummary = (syncInfo) => {
  const totalAgents = Number(syncInfo?.totalAgents) || 0;
  const syncedAgents = Number(syncInfo?.syncedAgents) || 0;
  const failedAgents = Number(syncInfo?.failedAgents) || 0;
  if (totalAgents <= 0) return 'Синхронизация политик: агенты не зарегистрированы';
  if (failedAgents <= 0) return `Синхронизация политик: ${syncedAgents}/${totalAgents} агентов обновлено`;
  return `Синхронизация политик: ${syncedAgents}/${totalAgents} агентов обновлено, с ошибками ${failedAgents}`;
};

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
  const [agentCommandsRefreshing, setAgentCommandsRefreshing] = useState(false);
  const [agentCommandStatusFilter, setAgentCommandStatusFilter] = useState('');
  const [agentCommandTypeFilter, setAgentCommandTypeFilter] = useState('');
  const [agentCommandFromFilter, setAgentCommandFromFilter] = useState('');
  const [agentCommandToFilter, setAgentCommandToFilter] = useState('');
  const [agentCommandForm, setAgentCommandForm] = useState(DEFAULT_AGENT_COMMAND_FORM);
  const [agentCommandSaving, setAgentCommandSaving] = useState(false);
  const [agentAdminReason, setAgentAdminReason] = useState('');
  const [desiredAgentVersion, setDesiredAgentVersion] = useState('');
  const [desiredVersionSaving, setDesiredVersionSaving] = useState(false);
  const [enqueueSelfUpdateCommand, setEnqueueSelfUpdateCommand] = useState(true);
  const [rolloutDesiredVersion, setRolloutDesiredVersion] = useState('');
  const [rolloutStrategy, setRolloutStrategy] = useState('canary');
  const [rolloutCanaryPercent, setRolloutCanaryPercent] = useState('10');
  const [rolloutStageSize, setRolloutStageSize] = useState('25');
  const [rolloutOnlineOnly, setRolloutOnlineOnly] = useState(true);
  const [rolloutAutoRollback, setRolloutAutoRollback] = useState(true);
  const [rolloutFailureRateThreshold, setRolloutFailureRateThreshold] = useState('0.3');
  const [rolloutMaxFailedAgents, setRolloutMaxFailedAgents] = useState('1');
  const [rolloutObservationSeconds, setRolloutObservationSeconds] = useState('0');
  const [rolloutPlanning, setRolloutPlanning] = useState(false);
  const [rolloutExecuting, setRolloutExecuting] = useState(false);
  const [rolloutPlan, setRolloutPlan] = useState(null);
  const [selectedRolloutStage, setSelectedRolloutStage] = useState(1);
  const [rolloutResult, setRolloutResult] = useState(null);
  const [alertRules, setAlertRules] = useState([]);
  const [alertRuleMetadata, setAlertRuleMetadata] = useState(null);
  const [alertRulesLoading, setAlertRulesLoading] = useState(false);
  const [alertRulesError, setAlertRulesError] = useState(null);
  const [alertRuleDialogOpen, setAlertRuleDialogOpen] = useState(false);
  const [editingAlertRuleId, setEditingAlertRuleId] = useState(null);
  const [alertRuleSaving, setAlertRuleSaving] = useState(false);
  const [alertRuleForm, setAlertRuleForm] = useState(DEFAULT_ALERT_RULE_FORM);
  const [auditEvents, setAuditEvents] = useState([]);
  const [auditTotalCount, setAuditTotalCount] = useState(0);
  const [auditLoading, setAuditLoading] = useState(false);
  const [auditError, setAuditError] = useState(null);
  const [auditExporting, setAuditExporting] = useState(false);
  const [auditPage, setAuditPage] = useState(1);
  const [auditPageSize, setAuditPageSize] = useState(25);
  const [auditActionFilter, setAuditActionFilter] = useState('');
  const [auditActorFilter, setAuditActorFilter] = useState('');
  const [auditSearchFilter, setAuditSearchFilter] = useState('');
  const [auditFromFilter, setAuditFromFilter] = useState('');
  const [auditToFilter, setAuditToFilter] = useState('');
  const [rbacRows, setRbacRows] = useState([]);
  const [rbacAvailablePermissions, setRbacAvailablePermissions] = useState([]);
  const [rbacLoading, setRbacLoading] = useState(false);
  const [rbacSaving, setRbacSaving] = useState(false);
  const [rbacError, setRbacError] = useState(null);
  const [newRbacRole, setNewRbacRole] = useState('');

  // General Settings
  const [generalSettings, setGeneralSettings] = useState({
    systemName: 'Система мониторинга активности',
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
    { id: 1, application: 'chrome.exe', description: 'Браузер Google Chrome' },
    { id: 2, application: 'explorer.exe', description: 'Проводник Windows' },
    { id: 3, application: 'winword.exe', description: 'Microsoft Word' }
  ]);

  // Blacklist entries
  const [blacklistEntries, setBlacklistEntries] = useState([
    { id: 1, application: 'torrent.exe', description: 'Торрент-клиент' },
    { id: 2, application: 'game.exe', description: 'Игровое приложение' }
  ]);
  const [listSettingsDirty, setListSettingsDirty] = useState(false);
  const [listSettingsSaving, setListSettingsSaving] = useState(false);
  const [policySyncInfo, setPolicySyncInfo] = useState(null);
  const [policySyncRunning, setPolicySyncRunning] = useState(false);
  const [listSettingsAutoSave, setListSettingsAutoSave] = useState(() => {
    try {
      const raw = localStorage.getItem(ACCESS_LIST_AUTOSAVE_STORAGE_KEY);
      return raw === null ? true : raw === 'true';
    } catch {
      return true;
    }
  });
  const accessListAutosaveTimerRef = useRef(null);
  const nextTempEntryIdRef = useRef(-1);

  const applySettingsPayload = useCallback((payload) => {
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
      setListSettingsDirty(false);
    }
    if (payload.blacklistEntries) {
      setBlacklistEntries(normalizeListEntries(payload.blacklistEntries));
      setListSettingsDirty(false);
    }
  }, []);

  const fetchSettings = useCallback(async ({ silent = false } = {}) => {
    try {
      if (!silent) setLoading(true);
      setError(null);
      const payload = await settingsAPI.getSettings();
      applySettingsPayload(payload);
    } catch (err) {
      setError(err?.response?.data?.message || err?.message || 'Не удалось загрузить настройки');
      console.error('Ошибка загрузки настроек:', err);
    } finally {
      setLoading(false);
    }
  }, [applySettingsPayload]);

  useEffect(() => {
    fetchSettings();
  }, [fetchSettings]);

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
      setDesiredAgentVersion('');
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
  }, [tabValue, selectedMonitoringAgentId, agentCommandStatusFilter, agentCommandTypeFilter, agentCommandFromFilter, agentCommandToFilter]);

  useEffect(() => {
    if (tabValue !== 2) return;
    fetchAlertRules();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tabValue]);

  useEffect(() => {
    if (tabValue !== 1) return;
    fetchRbacMatrix();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tabValue]);

  const handleTabChange = (event, newValue) => {
    setTabValue(newValue);
  };

  const applyPolicySyncInfo = (syncMeta) => {
    if (!syncMeta || typeof syncMeta !== 'object') return;

    const totalAgents = Math.max(0, Number(syncMeta.totalAgents) || 0);
    const syncedAgents = Math.max(0, Number(syncMeta.syncedAgents) || 0);
    const failedAgents = Math.max(0, Number(syncMeta.failedAgents) || 0);
    const errors = Array.isArray(syncMeta.errors)
      ? syncMeta.errors.filter((item) => typeof item === 'string' && item.trim().length > 0)
      : [];

    setPolicySyncInfo({
      status: String(syncMeta.status || '').toLowerCase() || (failedAgents > 0 ? 'partial' : 'ok'),
      totalAgents,
      syncedAgents,
      failedAgents,
      errors,
      timestamp: syncMeta.timestamp || new Date().toISOString(),
    });
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

      const { data: saved, policySync } = await settingsAPI.saveSettings(payload);
      applySettingsPayload(saved);
      applyPolicySyncInfo(policySync);

      setSuccess(`Настройки раздела «${category}» успешно сохранены`);
      if (typeof addNotification === 'function') {
        addNotification({
          type: 'success',
          title: `Настройки раздела «${category}» сохранены`,
          message: `Параметры раздела «${category}» были обновлены`,
          timestamp: new Date().toISOString()
        });
      }
      
      setTimeout(() => setSuccess(null), 3000);
    } catch (err) {
      setError(err?.response?.data?.message || err?.message || 'Не удалось сохранить настройки');
      console.error('Ошибка сохранения настроек:', err);
    } finally {
      setLoading(false);
    }
  };

  const handleAddWhitelistEntry = () => {
    const newEntry = {
      id: nextTempEntryIdRef.current--,
      application: '',
      description: ''
    };
    setWhitelistEntries((prev) => [...prev, newEntry]);
    setListSettingsDirty(true);
  };

  const handleUpdateWhitelistEntry = (id, field, value) => {
    setWhitelistEntries((prev) => prev.map(entry => 
      entry.id === id ? { ...entry, [field]: value } : entry
    ));
    setListSettingsDirty(true);
  };

  const handleDeleteWhitelistEntry = (id) => {
    setConfirmAction({
      type: 'delete_whitelist',
      id: id,
      message: 'Удалить эту запись белого списка?'
    });
    setConfirmDialogOpen(true);
  };

  const handleAddBlacklistEntry = () => {
    const newEntry = {
      id: nextTempEntryIdRef.current--,
      application: '',
      description: ''
    };
    setBlacklistEntries((prev) => [...prev, newEntry]);
    setListSettingsDirty(true);
  };

  const handleUpdateBlacklistEntry = (id, field, value) => {
    setBlacklistEntries((prev) => prev.map(entry => 
      entry.id === id ? { ...entry, [field]: value } : entry
    ));
    setListSettingsDirty(true);
  };

  const handleDeleteBlacklistEntry = (id) => {
    setConfirmAction({
      type: 'delete_blacklist',
      id: id,
      message: 'Удалить эту запись черного списка?'
    });
    setConfirmDialogOpen(true);
  };

  const handleConfirmAction = async () => {
    try {
      if (confirmAction?.type === 'delete_whitelist') {
        setWhitelistEntries((prev) => prev.filter(entry => entry.id !== confirmAction.id));
        setListSettingsDirty(true);
      } else if (confirmAction?.type === 'delete_blacklist') {
        setBlacklistEntries((prev) => prev.filter(entry => entry.id !== confirmAction.id));
        setListSettingsDirty(true);
      } else if (confirmAction?.type === 'delete_alert_rule') {
        await alertRulesAPI.deleteRule(confirmAction.id);
        setAlertRules((prev) => prev.filter((rule) => rule.id !== confirmAction.id));
      } else if (confirmAction?.type === 'reset_agent_policy') {
        await agentAPI.deleteAgentPolicy(confirmAction.agentId);
        if (confirmAction.agentId) {
          await fetchAgentControlData(confirmAction.agentId, { silent: true });
        }
        setSuccess('Политика агента сброшена к значениям по умолчанию');
        setTimeout(() => setSuccess(null), 3000);
      }
    } catch (err) {
      setError(err?.response?.data?.message || err?.message || 'Не удалось выполнить действие');
    } finally {
      setConfirmDialogOpen(false);
      setConfirmAction(null);
    }
  };

  const handleSaveAccessLists = async ({ silent = false } = {}) => {
    const hasDraftRows = [...whitelistEntries, ...blacklistEntries]
      .some((entry) => !String(entry?.application || '').trim());
    if (hasDraftRows) {
      setError('Заполните название приложения во всех строках или удалите пустые строки перед сохранением.');
      return;
    }

    try {
      setListSettingsSaving(true);
      setError(null);

      const payload = {
        generalSettings,
        securitySettings,
        notificationSettings,
        monitoringSettings,
        whitelistEntries: normalizeListEntries(whitelistEntries),
        blacklistEntries: normalizeListEntries(blacklistEntries),
      };

      const { data: saved, policySync } = await settingsAPI.saveSettings(payload);
      applySettingsPayload(saved);
      applyPolicySyncInfo(policySync);

      if (!silent) {
        setSuccess('Настройки белого/черного списка сохранены');
        setTimeout(() => setSuccess(null), 2500);
      }
    } catch (err) {
      setError(err?.response?.data?.message || err?.message || 'Не удалось сохранить настройки белого/черного списка');
      console.error('Ошибка сохранения списка доступа:', err);
    } finally {
      setListSettingsSaving(false);
    }
  };

  const handleReloadSettings = async () => {
    await fetchSettings();
    if (tabValue === 3) {
      await fetchMonitoringData({ silent: true });
    }
    setSuccess('Настройки обновлены');
    setTimeout(() => setSuccess(null), 2000);
  };

  const handleSyncPoliciesNow = async () => {
    try {
      setPolicySyncRunning(true);
      setError(null);

      const { data, policySync } = await settingsAPI.syncPolicies();
      const syncMeta = policySync || data || null;
      applyPolicySyncInfo(syncMeta);

      const failedAgents = Number(syncMeta?.failedAgents) || 0;
      const syncedAgents = Number(syncMeta?.syncedAgents) || 0;
      const totalAgents = Number(syncMeta?.totalAgents) || 0;

      setSuccess(
        failedAgents > 0
          ? `Синхронизация политик выполнена с ошибками: ${syncedAgents}/${totalAgents} обновлено`
          : `Синхронизация политик выполнена: ${syncedAgents}/${totalAgents} обновлено`
      );
      setTimeout(() => setSuccess(null), 2500);
    } catch (err) {
      setError(err?.response?.data?.message || err?.message || 'Не удалось синхронизировать политики');
      console.error('Ошибка синхронизации политик:', err);
    } finally {
      setPolicySyncRunning(false);
    }
  };

  useEffect(() => {
    try {
      localStorage.setItem(ACCESS_LIST_AUTOSAVE_STORAGE_KEY, String(Boolean(listSettingsAutoSave)));
    } catch {
      // ignore storage failures
    }
  }, [listSettingsAutoSave]);

  useEffect(() => {
    if (tabValue !== 4 || !listSettingsAutoSave || !listSettingsDirty) return undefined;
    const hasDraftRows = [...whitelistEntries, ...blacklistEntries]
      .some((entry) => !String(entry?.application || '').trim());
    if (hasDraftRows) return undefined;
    if (accessListAutosaveTimerRef.current) {
      window.clearTimeout(accessListAutosaveTimerRef.current);
    }
    accessListAutosaveTimerRef.current = window.setTimeout(() => {
      handleSaveAccessLists({ silent: true });
    }, 800);

    return () => {
      if (accessListAutosaveTimerRef.current) {
        window.clearTimeout(accessListAutosaveTimerRef.current);
        accessListAutosaveTimerRef.current = null;
      }
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tabValue, listSettingsAutoSave, listSettingsDirty, whitelistEntries, blacklistEntries]);

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
      setAlertRulesError(err?.response?.data?.message || err?.message || 'Не удалось загрузить правила тревог');
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
        setAlertRulesError('Название правила обязательно');
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
      setSuccess(`Правило тревоги ${editingAlertRuleId ? 'обновлено' : 'создано'} успешно`);
      setTimeout(() => setSuccess(null), 3000);
      if (typeof addNotification === 'function') {
        addNotification({
          type: 'success',
          title: `Правило тревоги ${editingAlertRuleId ? 'обновлено' : 'создано'}`,
          message: savedRule.name,
          timestamp: new Date().toISOString(),
        });
      }
    } catch (err) {
      setAlertRulesError(err?.response?.data?.message || err?.message || 'Не удалось сохранить правило тревоги');
    } finally {
      setAlertRuleSaving(false);
    }
  };

  const handleToggleAlertRule = async (rule) => {
    try {
      const updated = await alertRulesAPI.setEnabled(rule.id, !rule.enabled);
      setAlertRules((prev) => prev.map((item) => (item.id === updated.id ? updated : item)));
    } catch (err) {
      setAlertRulesError(err?.response?.data?.message || err?.message || 'Не удалось обновить статус правила');
    }
  };

  const handleDeleteAlertRule = (rule) => {
    setConfirmAction({
      type: 'delete_alert_rule',
      id: rule.id,
      message: `Удалить правило тревоги "${rule.name}"?`,
    });
    setConfirmDialogOpen(true);
  };

  const fetchRbacMatrix = async () => {
    try {
      setRbacLoading(true);
      setRbacError(null);
      const payload = await rbacAPI.getMatrix();
      setRbacRows(mapRolePermissionsToRows(payload?.rolePermissions));
      setRbacAvailablePermissions(Array.isArray(payload?.availablePermissions) ? payload.availablePermissions : []);
    } catch (err) {
      setRbacError(err?.response?.data?.message || err?.message || 'Не удалось загрузить RBAC-матрицу');
    } finally {
      setRbacLoading(false);
    }
  };

  const handleRbacRowChange = (role, value) => {
    const normalizedRole = String(role || '').trim().toLowerCase();
    if (!normalizedRole) return;

    setRbacRows((prev) => (Array.isArray(prev) ? prev : []).map((row) => (
      row.role === normalizedRole
        ? { ...row, permissionsCsv: value }
        : row
    )));
  };

  const handleAddRbacRole = () => {
    const normalizedRole = String(newRbacRole || '').trim().toLowerCase();
    if (!normalizedRole) return;

    setRbacRows((prev) => {
      const existing = Array.isArray(prev) ? prev : [];
      if (existing.some((row) => row.role === normalizedRole)) return existing;
      return [...existing, { role: normalizedRole, permissionsCsv: '' }]
        .sort((left, right) => left.role.localeCompare(right.role, 'ru-RU'));
    });
    setNewRbacRole('');
  };

  const handleDeleteRbacRole = (role) => {
    const normalizedRole = String(role || '').trim().toLowerCase();
    if (!normalizedRole) return;
    setRbacRows((prev) => (Array.isArray(prev) ? prev : []).filter((row) => row.role !== normalizedRole));
  };

  const handleSaveRbacMatrix = async () => {
    try {
      setRbacSaving(true);
      setRbacError(null);

      const payload = mapRowsToRolePermissions(rbacRows);
      const response = await rbacAPI.saveMatrix(payload);
      setRbacRows(mapRolePermissionsToRows(response?.rolePermissions || payload));

      setSuccess('RBAC-матрица сохранена');
      setTimeout(() => setSuccess(null), 2500);
    } catch (err) {
      setRbacError(err?.response?.data?.message || err?.message || 'Не удалось сохранить RBAC-матрицу');
    } finally {
      setRbacSaving(false);
    }
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
      setMonitoringDataError(err?.response?.data?.message || err?.message || 'Не удалось загрузить данные мониторинга');
    } finally {
      setMonitoringDataLoading(false);
    }
  };

  const buildAuditQuery = useCallback((overrides = {}) => {
    const query = {
      page: overrides.page ?? auditPage,
      pageSize: overrides.pageSize ?? auditPageSize,
    };

    const actionFilter = overrides.action ?? auditActionFilter;
    const actorFilter = overrides.actor ?? auditActorFilter;
    const searchFilter = overrides.q ?? auditSearchFilter;
    const fromFilter = overrides.fromRaw ?? auditFromFilter;
    const toFilter = overrides.toRaw ?? auditToFilter;

    if (actionFilter) query.action = actionFilter;
    if (actorFilter) query.actor = actorFilter;
    if (searchFilter) query.q = searchFilter;

    const fromIso = toUtcIso(fromFilter);
    const toIso = toUtcIso(toFilter);
    if (fromIso) query.from = fromIso;
    if (toIso) query.to = toIso;

    return query;
  }, [
    auditPage,
    auditPageSize,
    auditActionFilter,
    auditActorFilter,
    auditSearchFilter,
    auditFromFilter,
    auditToFilter,
  ]);

  const fetchAuditEvents = useCallback(async ({ silent = false, page, pageSize, queryOverrides } = {}) => {
    try {
      if (!silent) setAuditLoading(true);
      setAuditError(null);

      const response = await auditAPI.getEvents(buildAuditQuery({ page, pageSize, ...(queryOverrides || {}) }));
      setAuditEvents(response?.events || []);
      setAuditTotalCount(response?.totalCount || 0);
    } catch (err) {
      setAuditError(err?.response?.data?.message || err?.message || 'Не удалось загрузить журнал аудита');
    } finally {
      setAuditLoading(false);
    }
  }, [buildAuditQuery]);

  useEffect(() => {
    if (tabValue !== 5) return;
    fetchAuditEvents();
  }, [tabValue, auditPage, auditPageSize, fetchAuditEvents]);

  const handleApplyAuditFilters = () => {
    setAuditPage(1);
    fetchAuditEvents({ page: 1 });
  };

  const handleResetAuditFilters = () => {
    setAuditActionFilter('');
    setAuditActorFilter('');
    setAuditSearchFilter('');
    setAuditFromFilter('');
    setAuditToFilter('');
    setAuditPage(1);
    fetchAuditEvents({
      page: 1,
      queryOverrides: {
        action: '',
        actor: '',
        q: '',
        fromRaw: '',
        toRaw: '',
      },
    });
  };

  const handleExportAuditCsv = async () => {
    try {
      setAuditExporting(true);
      setAuditError(null);

      const response = await auditAPI.getEvents(buildAuditQuery({ page: 1, pageSize: 1000 }));
      const rows = response?.events || [];
      const csvText = buildAuditCsv(rows);
      const blob = new Blob([csvText], { type: 'text/csv;charset=utf-8;' });
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = `audit-events-${new Date().toISOString().slice(0, 19).replaceAll(':', '-')}.csv`;
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);
      URL.revokeObjectURL(url);
    } catch (err) {
      setAuditError(err?.response?.data?.message || err?.message || 'Не удалось экспортировать журнал аудита');
    } finally {
      setAuditExporting(false);
    }
  };

  const upsertCommandInState = useCallback((command) => {
    if (!command || typeof command !== 'object') return;

    setAgentCommands((prev) => {
      const existing = Array.isArray(prev) ? prev : [];
      const withoutCurrent = existing.filter((item) => item.id !== command.id);
      return [command, ...withoutCurrent];
    });

    setAgentCommandsTotal((prevTotal) => {
      if (typeof prevTotal === 'number' && prevTotal > 0) return prevTotal;
      return 1;
    });
  }, []);

  const buildAgentCommandQuery = useCallback(() => {
    const query = { page: 1, pageSize: 30 };
    if (agentCommandStatusFilter) query.status = agentCommandStatusFilter;
    if (agentCommandTypeFilter) query.type = agentCommandTypeFilter;

    const createdFrom = toUtcIso(agentCommandFromFilter);
    const createdTo = toUtcIso(agentCommandToFilter);
    if (createdFrom) query.from = createdFrom;
    if (createdTo) query.to = createdTo;

    return query;
  }, [agentCommandStatusFilter, agentCommandTypeFilter, agentCommandFromFilter, agentCommandToFilter]);

  const fetchAgentCommands = useCallback(async (agentId, { silent = false } = {}) => {
    if (!agentId) return;

    try {
      if (!silent) setAgentCommandsRefreshing(true);
      setAgentControlError(null);

      const commandsResponse = await agentAPI.getAgentCommands(agentId, buildAgentCommandQuery());
      setAgentCommands(commandsResponse?.commands || []);
      setAgentCommandsTotal(commandsResponse?.totalCount || 0);
    } catch (err) {
      setAgentControlError(err?.response?.data?.message || err?.message || 'Не удалось загрузить команды агента');
    } finally {
      setAgentCommandsRefreshing(false);
    }
  }, [buildAgentCommandQuery]);

  const fetchAgentControlData = async (agentId, { silent = false } = {}) => {
    if (!agentId) return;

    try {
      if (!silent) setAgentControlLoading(true);
      setAgentControlError(null);

      const [policyResult, commandsResult] = await Promise.allSettled([
        agentAPI.getAgentPolicy(agentId),
        agentAPI.getAgentCommands(agentId, buildAgentCommandQuery()),
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
      setAgentControlError(err?.response?.data?.message || err?.message || 'Не удалось загрузить данные управления агентом');
    } finally {
      setAgentControlLoading(false);
    }
  };

  useEffect(() => {
    if (tabValue !== 3 || !selectedMonitoringAgentId) return undefined;

    const shouldPoll =
      agentCommandSaving
      || agentActionLoading
      || agentCommands.some((command) => isInFlightCommand(command?.status));

    if (!shouldPoll) return undefined;

    const timerId = window.setInterval(() => {
      if (document.hidden) return;
      fetchAgentCommands(selectedMonitoringAgentId, { silent: true });
    }, 2500);

    return () => window.clearInterval(timerId);
  }, [
    tabValue,
    selectedMonitoringAgentId,
    agentCommandSaving,
    agentActionLoading,
    agentCommands,
    fetchAgentCommands,
  ]);

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
      setSuccess('Политика агента успешно сохранена');
      setTimeout(() => setSuccess(null), 3000);
    } catch (err) {
      setAgentControlError(err?.response?.data?.message || err?.message || 'Не удалось сохранить политику агента');
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
        setAgentControlError('Тип команды обязателен');
        return;
      }

      const payloadJson = String(agentCommandForm.payloadJson || '').trim() || '{}';
      try {
        JSON.parse(payloadJson);
      } catch {
        setAgentControlError('Payload команды должен быть валидным JSON');
        return;
      }

      const commandKey = `panel-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`;
      const createdCommand = await agentAPI.createAgentCommand(selectedMonitoringAgentId, {
        type,
        payloadJson,
        requestedBy: agentCommandForm.requestedBy || undefined,
        commandKey,
      });
      if (agentCommandStatusFilter) {
        setAgentCommandStatusFilter('');
      }
      upsertCommandInState(createdCommand);

      setSuccess('Команда агента поставлена в очередь');
      setTimeout(() => setSuccess(null), 3000);
      await fetchAgentCommands(selectedMonitoringAgentId, { silent: true });
    } catch (err) {
      setAgentControlError(err?.response?.data?.message || err?.message || 'Не удалось поставить команду в очередь');
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
        || (blocked ? 'Заблокировано администратором' : 'Разблокировано администратором');

      if (blocked) {
        const response = await agentAPI.blockWorkstation(selectedMonitoringAgentId, reason);
        if (response?.command) {
          if (agentCommandStatusFilter) {
            setAgentCommandStatusFilter('');
          }
          upsertCommandInState(response.command);
        }
      } else {
        const response = await agentAPI.unblockWorkstation(selectedMonitoringAgentId, reason);
        if (response?.command) {
          if (agentCommandStatusFilter) {
            setAgentCommandStatusFilter('');
          }
          upsertCommandInState(response.command);
        }
      }

      setSuccess(blocked ? 'Команда блокировки поставлена в очередь' : 'Команда разблокировки поставлена в очередь');
      setTimeout(() => setSuccess(null), 3000);

      await Promise.all([
        fetchAgentControlData(selectedMonitoringAgentId, { silent: true }),
        fetchAgentCommands(selectedMonitoringAgentId, { silent: true }),
        fetchMonitoringData({ silent: true }),
      ]);
    } catch (err) {
      setAgentControlError(err?.response?.data?.message || err?.message || 'Не удалось отправить команду блокировки/разблокировки');
    } finally {
      setAgentActionLoading(false);
    }
  };

  const handleRetryAgentCommand = async (command) => {
    if (!selectedMonitoringAgentId || !command?.id) return;

    try {
      setAgentActionLoading(true);
      setAgentControlError(null);

      const response = await agentAPI.retryAgentCommand(selectedMonitoringAgentId, command.id);
      if (agentCommandStatusFilter) {
        setAgentCommandStatusFilter('');
      }
      if (response?.command) {
        upsertCommandInState(response.command);
      }

      setSuccess(response?.message || `Команда #${command.id} поставлена на повтор`);
      setTimeout(() => setSuccess(null), 3000);
      await fetchAgentCommands(selectedMonitoringAgentId, { silent: true });
    } catch (err) {
      setAgentControlError(err?.response?.data?.message || err?.message || 'Не удалось поставить команду на повтор');
    } finally {
      setAgentActionLoading(false);
    }
  };

  const handleSaveDesiredAgentVersion = async () => {
    if (!selectedMonitoringAgentId) return;

    try {
      setDesiredVersionSaving(true);
      setAgentControlError(null);

      const desiredVersion = String(desiredAgentVersion || '').trim();
      const response = await agentAPI.setDesiredVersion(selectedMonitoringAgentId, {
        desiredVersion,
        enqueueSelfUpdate: desiredVersion ? enqueueSelfUpdateCommand : false,
      });

      if (response?.agent) {
        setMonitoringAgents((prev) => (Array.isArray(prev) ? prev : []).map((item) => (
          item.id === selectedMonitoringAgentId
            ? { ...item, ...response.agent }
            : item
        )));
        setDesiredAgentVersion(response.agent.desiredVersion || '');
      }

      if (response?.command) {
        if (agentCommandStatusFilter) {
          setAgentCommandStatusFilter('');
        }
        upsertCommandInState(response.command);
      }

      setSuccess(response?.message || 'Целевая версия агента обновлена');
      setTimeout(() => setSuccess(null), 3000);
      await fetchAgentCommands(selectedMonitoringAgentId, { silent: true });
    } catch (err) {
      setAgentControlError(err?.response?.data?.message || err?.message || 'Не удалось обновить целевую версию агента');
    } finally {
      setDesiredVersionSaving(false);
    }
  };

  const handlePlanRollout = async () => {
    const desiredVersion = String(rolloutDesiredVersion || '').trim();
    if (!desiredVersion) {
      setAgentControlError('Укажите целевую версию для rollout');
      return;
    }

    try {
      setRolloutPlanning(true);
      setAgentControlError(null);
      setRolloutResult(null);

      const plan = await agentAPI.planRollout({
        desiredVersion,
        strategy: rolloutStrategy,
        canaryPercent: Math.max(1, Number(rolloutCanaryPercent) || 10),
        stageSize: Math.max(1, Number(rolloutStageSize) || 25),
        onlineOnly: rolloutOnlineOnly,
      });

      setRolloutPlan(plan || null);
      setSelectedRolloutStage(1);
      setSuccess('План rollout рассчитан');
      setTimeout(() => setSuccess(null), 2000);
    } catch (err) {
      setAgentControlError(err?.response?.data?.message || err?.message || 'Не удалось рассчитать rollout');
    } finally {
      setRolloutPlanning(false);
    }
  };

  const handleExecuteRollout = async () => {
    const desiredVersion = String(rolloutDesiredVersion || '').trim();
    if (!desiredVersion) {
      setAgentControlError('Укажите целевую версию для rollout');
      return;
    }

    const stages = Array.isArray(rolloutPlan?.stages) ? rolloutPlan.stages : [];
    const selectedStage = stages.find((stage) => Number(stage?.stage) === Number(selectedRolloutStage))
      || stages[0];

    if (!selectedStage || !Array.isArray(selectedStage.agentIds) || selectedStage.agentIds.length === 0) {
      setAgentControlError('Нет выбранного этапа rollout с агентами');
      return;
    }

    try {
      setRolloutExecuting(true);
      setAgentControlError(null);

      const result = await agentAPI.executeRollout({
        desiredVersion,
        agentIds: selectedStage.agentIds,
        autoRollbackEnabled: rolloutAutoRollback,
        observationSeconds: Math.max(0, Number(rolloutObservationSeconds) || 0),
        failureRateThreshold: Math.max(0.01, Number(rolloutFailureRateThreshold) || 0.3),
        maxFailedAgents: Math.max(0, Number(rolloutMaxFailedAgents) || 1),
        enqueueSelfUpdate: true,
      });

      setRolloutResult(result || null);
      setSuccess(
        result?.autoRollback?.rollbackTriggered
          ? `Этап ${selectedStage.stage} выполнен, инициирован rollback`
          : `Этап ${selectedStage.stage} выполнен`
      );
      setTimeout(() => setSuccess(null), 3000);

      const nextStage = stages.find((stage) => Number(stage?.stage) > Number(selectedStage.stage));
      if (nextStage) {
        setSelectedRolloutStage(Number(nextStage.stage));
      }

      await fetchMonitoringData({ silent: true });
      if (selectedMonitoringAgentId) {
        await fetchAgentCommands(selectedMonitoringAgentId, { silent: true });
      }
    } catch (err) {
      setAgentControlError(err?.response?.data?.message || err?.message || 'Не удалось выполнить rollout');
    } finally {
      setRolloutExecuting(false);
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
  const auditActionOptions = Array.from(new Set((auditEvents || []).map((event) => event?.action).filter(Boolean))).sort();
  const auditActorOptions = Array.from(new Set((auditEvents || []).map((event) => event?.actor).filter(Boolean))).sort();
  const auditTotalPages = Math.max(1, Math.ceil((auditTotalCount || 0) / (auditPageSize || 25)));
  const rolloutStages = Array.isArray(rolloutPlan?.stages) ? rolloutPlan.stages : [];

  useEffect(() => {
    if (tabValue !== 3) return;
    setDesiredAgentVersion(selectedMonitoringAgent?.desiredVersion || '');
  }, [tabValue, selectedMonitoringAgentId, selectedMonitoringAgent?.desiredVersion]);

  useEffect(() => {
    if (tabValue !== 3) return;
    if (String(rolloutDesiredVersion || '').trim()) return;
    setRolloutDesiredVersion(selectedMonitoringAgent?.desiredVersion || '');
  }, [tabValue, selectedMonitoringAgentId, selectedMonitoringAgent?.desiredVersion, rolloutDesiredVersion]);

  return (
    <Box>
      <Box display="flex" justifyContent="space-between" alignItems="center" mb={3}>
        <Typography variant="h4">Системные настройки</Typography>
        <Button
          variant="outlined"
          startIcon={<Refresh />}
          onClick={handleReloadSettings}
          disabled={loading}
        >
          Обновить настройки
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

      {policySyncInfo && (
        <Alert
          severity={getPolicySyncSeverity(policySyncInfo)}
          sx={{ mb: 2 }}
          action={(
            <Button
              color="inherit"
              size="small"
              onClick={handleSyncPoliciesNow}
              disabled={policySyncRunning || loading || listSettingsSaving}
            >
              {policySyncRunning ? 'Синхронизация...' : 'Пересинхронизировать'}
            </Button>
          )}
        >
          <Typography variant="body2" fontWeight={600}>
            {getPolicySyncSummary(policySyncInfo)}
          </Typography>
          <Typography variant="caption" display="block">
            Последняя синхронизация: {policySyncInfo.timestamp ? new Date(policySyncInfo.timestamp).toLocaleString('ru-RU') : '-'}
          </Typography>
          {Array.isArray(policySyncInfo.errors) && policySyncInfo.errors.length > 0 && (
            <Typography variant="caption" display="block">
              Ошибки: {policySyncInfo.errors.slice(0, 2).join(' | ')}
              {policySyncInfo.errors.length > 2 ? ' ...' : ''}
            </Typography>
          )}
        </Alert>
      )}

      <Tabs value={tabValue} onChange={handleTabChange} sx={{ mb: 3 }}>
        <Tab label="Общие" icon={<SettingsIcon />} />
        <Tab label="Безопасность" icon={<Security />} />
        <Tab label="Уведомления" icon={<Notifications />} />
        <Tab label="Мониторинг" icon={<NetworkCheck />} />
        <Tab label="Белый/черный список" icon={<Storage />} />
        <Tab label="Аудит" icon={<Security />} />
      </Tabs>

      {/* General Settings */}
      {tabValue === 0 && (
        <Card>
          <CardContent>
            <Typography variant="h6" gutterBottom>
              Общая конфигурация
            </Typography>
            <Grid container spacing={3}>
              <Grid item xs={12} md={6}>
                <TextField
                  fullWidth
                  label="Имя системы"
                  value={generalSettings.systemName}
                  onChange={(e) => setGeneralSettings({ ...generalSettings, systemName: e.target.value })}
                />
              </Grid>
              <Grid item xs={12} md={6}>
                <FormControl fullWidth>
                  <InputLabel>Уровень логирования</InputLabel>
                  <Select
                    value={generalSettings.logLevel}
                    label="Уровень логирования"
                    onChange={(e) => setGeneralSettings({ ...generalSettings, logLevel: e.target.value })}
                  >
                    <MenuItem value="Debug">Отладка</MenuItem>
                    <MenuItem value="Info">Инфо</MenuItem>
                    <MenuItem value="Warning">Предупреждение</MenuItem>
                    <MenuItem value="Error">Ошибка</MenuItem>
                  </Select>
                </FormControl>
              </Grid>
              <Grid item xs={12} md={6}>
                <TextField
                  fullWidth
                  label="Хранение логов (дни)"
                  type="number"
                  value={generalSettings.maxLogRetention}
                  onChange={(e) => setGeneralSettings({ ...generalSettings, maxLogRetention: e.target.value })}
                />
              </Grid>
              <Grid item xs={12} md={6}>
                <TextField
                  fullWidth
                  label="Таймаут сессии (минуты)"
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
                  label="Включить аудит"
                />
              </Grid>
            </Grid>
            <Box mt={3}>
              <Button
                variant="contained"
                startIcon={<Save />}
                onClick={() => handleSaveSettings('Общие')}
                disabled={loading}
              >
                Сохранить общие настройки
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
              Настройки безопасности
            </Typography>
            <Grid container spacing={3}>
              <Grid item xs={12} md={6}>
                <TextField
                  fullWidth
                  label="Минимальная длина пароля"
                  type="number"
                  value={securitySettings.passwordMinLength}
                  onChange={(e) => setSecuritySettings({ ...securitySettings, passwordMinLength: e.target.value })}
                />
              </Grid>
              <Grid item xs={12} md={6}>
                <TextField
                  fullWidth
                  label="Максимум попыток входа"
                  type="number"
                  value={securitySettings.maxLoginAttempts}
                  onChange={(e) => setSecuritySettings({ ...securitySettings, maxLoginAttempts: e.target.value })}
                />
              </Grid>
              <Grid item xs={12} md={6}>
                <TextField
                  fullWidth
                  label="Блокировка (минуты)"
                  type="number"
                  value={securitySettings.lockoutDurationMinutes}
                  onChange={(e) => setSecuritySettings({ ...securitySettings, lockoutDurationMinutes: e.target.value })}
                />
              </Grid>
              <Grid item xs={12} md={6}>
                <TextField
                  fullWidth
                  label="Срок действия JWT (часы)"
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
                  label="Требовать спецсимволы в пароле"
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
                  label="Включить двухфакторную аутентификацию"
                />
              </Grid>
            </Grid>
            <Box mt={3}>
              <Button
                variant="contained"
                startIcon={<Save />}
                onClick={() => handleSaveSettings('Безопасность')}
                disabled={loading}
              >
                Сохранить настройки безопасности
              </Button>
            </Box>

            <Divider sx={{ my: 3 }} />

            <Box display="flex" justifyContent="space-between" alignItems="center" flexWrap="wrap" gap={2} mb={2}>
              <Box>
                <Typography variant="h6">RBAC матрица ролей</Typography>
                <Typography variant="body2" color="text.secondary">
                  Управление правами ролей в БД gateway runtime.
                </Typography>
              </Box>
              <Stack direction="row" spacing={1}>
                <Button
                  variant="outlined"
                  startIcon={<Refresh />}
                  onClick={() => fetchRbacMatrix()}
                  disabled={rbacLoading || rbacSaving}
                >
                  Обновить
                </Button>
                <Button
                  variant="contained"
                  startIcon={<Save />}
                  onClick={handleSaveRbacMatrix}
                  disabled={rbacLoading || rbacSaving}
                >
                  {rbacSaving ? 'Сохранение...' : 'Сохранить RBAC'}
                </Button>
              </Stack>
            </Box>

            {rbacError && (
              <Alert severity="warning" sx={{ mb: 2 }}>
                {rbacError}
              </Alert>
            )}

            {rbacLoading && <LinearProgress sx={{ mb: 2, borderRadius: 999 }} />}

            <Stack spacing={1.5} sx={{ mb: 2 }}>
              {rbacRows.length === 0 ? (
                <Paper variant="outlined" sx={{ p: 2 }}>
                  <Typography variant="body2" color="text.secondary">
                    Роли не настроены. Добавьте роль и задайте права.
                  </Typography>
                </Paper>
              ) : (
                rbacRows.map((row) => (
                  <Paper key={row.role} variant="outlined" sx={{ p: 2 }}>
                    <Box display="flex" justifyContent="space-between" alignItems="center" gap={1} mb={1}>
                      <Chip label={row.role} color="primary" variant="outlined" />
                      <Button
                        size="small"
                        color="error"
                        onClick={() => handleDeleteRbacRole(row.role)}
                        disabled={rbacSaving}
                      >
                        Удалить роль
                      </Button>
                    </Box>
                    <TextField
                      fullWidth
                      multiline
                      minRows={2}
                      label="Права (через запятую)"
                      placeholder="dashboard.*, audit.getevents, reports.*"
                      value={row.permissionsCsv}
                      onChange={(e) => handleRbacRowChange(row.role, e.target.value)}
                    />
                  </Paper>
                ))
              )}
            </Stack>

            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1} sx={{ mb: 2 }}>
              <TextField
                size="small"
                label="Новая роль"
                value={newRbacRole}
                onChange={(e) => setNewRbacRole(e.target.value)}
                placeholder="например, auditor"
              />
              <Button variant="outlined" startIcon={<Add />} onClick={handleAddRbacRole} disabled={rbacSaving}>
                Добавить роль
              </Button>
            </Stack>

            {rbacAvailablePermissions.length > 0 && (
              <Paper variant="outlined" sx={{ p: 2 }}>
                <Typography variant="subtitle2" gutterBottom>
                  Доступные permissions из API контроллеров
                </Typography>
                <Stack direction="row" spacing={0.5} useFlexGap flexWrap="wrap">
                  {rbacAvailablePermissions.map((permission) => (
                    <Chip key={permission} size="small" label={permission} />
                  ))}
                </Stack>
              </Paper>
            )}
          </CardContent>
        </Card>
      )}

      {/* Notification Settings */}
      {tabValue === 2 && (
        <Card>
          <CardContent>
            <Typography variant="h6" gutterBottom>
              Настройки уведомлений
            </Typography>
            <Grid container spacing={3}>
              <Grid item xs={12} md={6}>
                <TextField
                  fullWidth
                  label="Почта для уведомлений"
                  type="email"
                  value={notificationSettings.notificationEmail}
                  onChange={(e) => setNotificationSettings({ ...notificationSettings, notificationEmail: e.target.value })}
                />
              </Grid>
              <Grid item xs={12} md={6}>
                <TextField
                  fullWidth
                  label="SMTP-сервер"
                  value={notificationSettings.smtpServer}
                  onChange={(e) => setNotificationSettings({ ...notificationSettings, smtpServer: e.target.value })}
                />
              </Grid>
              <Grid item xs={12} md={6}>
                <TextField
                  fullWidth
                  label="SMTP-порт"
                  type="number"
                  value={notificationSettings.smtpPort}
                  onChange={(e) => setNotificationSettings({ ...notificationSettings, smtpPort: e.target.value })}
                />
              </Grid>
              <Grid item xs={12} md={6}>
                <TextField
                  fullWidth
                  label="Порог тревог"
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
                  label="Почтовые уведомления"
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
                  label="SMS-уведомления"
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
                  label="Push-уведомления"
                />
              </Grid>
            </Grid>

            <Divider sx={{ my: 3 }} />

            <Box display="flex" justifyContent="space-between" alignItems="center" flexWrap="wrap" gap={2} mb={2}>
              <Box>
                <Typography variant="h6">Правила тревог</Typography>
                <Typography variant="body2" color="text.secondary">
                  Пороговые правила для аномалий, блокировок и всплесков риска.
                </Typography>
              </Box>
              <Stack direction="row" spacing={1}>
                <Button
                  variant="outlined"
                  startIcon={<Refresh />}
                  onClick={() => fetchAlertRules()}
                  disabled={alertRulesLoading}
                >
                  Обновить правила
                </Button>
                <Button variant="contained" startIcon={<Add />} onClick={openCreateAlertRuleDialog}>
                  Добавить правило
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
                    <TableCell>Правило</TableCell>
                    <TableCell>Условие</TableCell>
                    <TableCell>Область</TableCell>
                    <TableCell>Каналы</TableCell>
                    <TableCell>Статус</TableCell>
                    <TableCell align="right">Действия</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {alertRules.length === 0 ? (
                    <TableRow>
                      <TableCell colSpan={6} align="center">
                        Правил тревог пока нет. Создайте правило для автоматических уведомлений.
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
                              label={(() => {
                                const severity = String(rule.severity || '').toLowerCase();
                                if (severity === 'critical' || severity === 'high') return 'Высокая';
                                if (severity === 'medium') return 'Средняя';
                                if (severity === 'low') return 'Низкая';
                                return String(rule.severity || 'Неизвестно');
                              })()}
                              color={
                                String(rule.severity).toLowerCase() === 'critical' ? 'error'
                                  : String(rule.severity).toLowerCase() === 'high' ? 'warning'
                                    : String(rule.severity).toLowerCase() === 'low' ? 'success'
                                      : 'default'
                              }
                            />
                            <Chip size="small" variant="outlined" label={`${rule.windowMinutes || '-'}м`} />
                            <Chip size="small" variant="outlined" label={`${rule.cooldownMinutes || 0}м пауза`} />
                          </Stack>
                        </TableCell>
                        <TableCell>
                          {(ALERT_RULE_LABELS[rule.metric] || rule.metric)} {OPERATOR_LABELS[rule.operator] || rule.operator} {rule.threshold}
                        </TableCell>
                        <TableCell>
                          <Stack direction="row" spacing={0.5} useFlexGap flexWrap="wrap">
                            {rule.activityType && <Chip size="small" variant="outlined" label={`Тип:${rule.activityType}`} />}
                            {rule.userId && <Chip size="small" variant="outlined" label={`Пользователь:${rule.userId}`} />}
                            {rule.computerId && <Chip size="small" variant="outlined" label={`ПК:${rule.computerId}`} />}
                            {!rule.activityType && !rule.userId && !rule.computerId && (
                              <Typography variant="caption" color="text.secondary">Глобально</Typography>
                            )}
                          </Stack>
                        </TableCell>
                        <TableCell>
                          <Stack direction="row" spacing={0.5} useFlexGap flexWrap="wrap">
                            {rule.notifyInApp && <Chip size="small" label="В приложении" />}
                            {rule.notifyEmail && <Chip size="small" label="Почта" />}
                            {!rule.notifyInApp && !rule.notifyEmail && <Chip size="small" variant="outlined" label="Нет" />}
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
                            label={rule.enabled ? 'Включено' : 'Отключено'}
                          />
                        </TableCell>
                        <TableCell align="right">
                          <Tooltip title="Редактировать правило">
                            <IconButton size="small" onClick={() => openEditAlertRuleDialog(rule)}>
                              <Edit fontSize="small" />
                            </IconButton>
                          </Tooltip>
                          <Tooltip title="Удалить правило">
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
                onClick={() => handleSaveSettings('Уведомления')}
                disabled={loading}
              >
                Сохранить настройки уведомлений
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
                    <Typography variant="h6">Состояние системы онлайн</Typography>
                    <Typography variant="body2" color="text.secondary">
                      Статус в реальном времени из агрегированных проверок шлюза (`/api/system/health`) и сервиса агентов.
                    </Typography>
                    <Typography variant="caption" color="text.secondary">
                      Последнее обновление: {monitoringLastUpdated ? monitoringLastUpdated.toLocaleTimeString('ru-RU') : '-'}
                    </Typography>
                  </Box>
                  <Button
                    variant="outlined"
                    startIcon={<Refresh />}
                    onClick={() => fetchMonitoringData()}
                    disabled={monitoringDataLoading}
                  >
                    {monitoringDataLoading ? 'Обновление...' : 'Обновить состояние'}
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
                      <Typography variant="body2" color="text.secondary">Общий статус</Typography>
                      <Chip
                        size="small"
                        color={systemHealth?.status === 'healthy' ? 'success' : systemHealth?.status === 'degraded' ? 'warning' : 'error'}
                        label={getStatusLabel(systemHealth?.status)}
                        sx={{ mt: 1 }}
                      />
                    </Paper>
                  </Grid>
                  <Grid item xs={12} sm={6} md={3}>
                    <Paper sx={{ p: 2 }}>
                      <Typography variant="body2" color="text.secondary">Работают сервисов</Typography>
                      <Typography variant="h5">{healthyServicesCount}/{healthServices.length}</Typography>
                    </Paper>
                  </Grid>
                  <Grid item xs={12} sm={6} md={3}>
                    <Paper sx={{ p: 2 }}>
                      <Typography variant="body2" color="text.secondary">Всего агентов</Typography>
                      <Typography variant="h5">{monitoringAgents.length}</Typography>
                    </Paper>
                  </Grid>
                  <Grid item xs={12} sm={6} md={3}>
                    <Paper sx={{ p: 2 }}>
                      <Typography variant="body2" color="text.secondary">Агенты онлайн</Typography>
                      <Typography variant="h5">{agentStatusSummary.online || agentStatusSummary.active || 0}</Typography>
                    </Paper>
                  </Grid>
                </Grid>

                <Grid container spacing={2}>
                  <Grid item xs={12} lg={7}>
                    <Typography variant="subtitle1" gutterBottom>Проверки сервисов</Typography>
                    <TableContainer component={Paper} variant="outlined">
                      <Table size="small">
                        <TableHead>
                          <TableRow>
                            <TableCell>Сервис</TableCell>
                            <TableCell>Статус</TableCell>
                            <TableCell align="right">Задержка</TableCell>
                            <TableCell align="right">HTTP</TableCell>
                          </TableRow>
                        </TableHead>
                        <TableBody>
                          {healthServices.length === 0 ? (
                            <TableRow>
                              <TableCell colSpan={4} align="center">Данные о состоянии еще не загружены</TableCell>
                            </TableRow>
                          ) : healthServices.map((service) => (
                            <TableRow key={service.name} hover>
                              <TableCell>{service.name}</TableCell>
                              <TableCell>
                                <Chip
                                  size="small"
                                  color={service.status === 'healthy' ? 'success' : service.status === 'degraded' ? 'warning' : 'error'}
                                  label={getStatusLabel(service.status)}
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
                    <Typography variant="subtitle1" gutterBottom>Агенты и сигналы heartbeat</Typography>
                    <TableContainer component={Paper} variant="outlined">
                      <Table size="small">
                        <TableHead>
                          <TableRow>
                            <TableCell>ID</TableCell>
                            <TableCell>Статус</TableCell>
                            <TableCell>Версия</TableCell>
                            <TableCell>Последний heartbeat</TableCell>
                          </TableRow>
                        </TableHead>
                        <TableBody>
                          {monitoringAgents.length === 0 ? (
                            <TableRow>
                              <TableCell colSpan={4} align="center">Агенты не найдены</TableCell>
                            </TableRow>
                          ) : monitoringAgents.slice(0, 20).map((agent) => (
                            <TableRow key={agent.id} hover>
                              <TableCell>{agent.id}</TableCell>
                              <TableCell>
                                <Chip
                                  size="small"
                                  color={String(agent.status || '').toLowerCase().includes('online') || String(agent.status || '').toLowerCase().includes('active') ? 'success' : 'warning'}
                                  label={getStatusLabel(agent.status)}
                                />
                              </TableCell>
                              <TableCell>{agent.version || '-'}</TableCell>
                              <TableCell>
                                {agent.lastHeartbeat ? new Date(agent.lastHeartbeat).toLocaleString('ru-RU') : '-'}
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
                    <Typography variant="h6">Управление агентом</Typography>
                    <Typography variant="body2" color="text.secondary">
                      Настройка политики сбора и отправка прямых команд (блокировка/разблокировка, обновление политики) для выбранного локального агента.
                    </Typography>
                  </Box>
                  <Stack direction="row" spacing={1}>
                    <Button
                      variant="outlined"
                      startIcon={<Refresh />}
                      onClick={() => selectedMonitoringAgentId && fetchAgentControlData(selectedMonitoringAgentId)}
                      disabled={!selectedMonitoringAgentId || agentControlLoading}
                    >
                      {agentControlLoading ? 'Обновление...' : 'Обновить агента'}
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
                    Агенты пока недоступны. Запустите локальный агент и дождитесь регистрации сигнала heartbeat.
                  </Alert>
                ) : (
                  <Grid container spacing={2}>
                    <Grid item xs={12}>
                      <Grid container spacing={2} alignItems="center">
                        <Grid item xs={12} md={4}>
                          <FormControl fullWidth size="small">
                            <InputLabel>Выбранный агент</InputLabel>
                            <Select
                              label="Выбранный агент"
                              value={selectedMonitoringAgentId || ''}
                              onChange={(e) => setSelectedMonitoringAgentId(Number(e.target.value))}
                            >
                              {monitoringAgents.map((agent) => (
                                <MenuItem key={agent.id} value={agent.id}>
                                  #{agent.id} · ПК {agent.computerId ?? '-'} · {getStatusLabel(agent.status)}
                                </MenuItem>
                              ))}
                            </Select>
                          </FormControl>
                        </Grid>

                        <Grid item xs={12} md={8}>
                          <Stack direction="row" spacing={1} useFlexGap flexWrap="wrap" alignItems="center">
                            <Chip
                              size="small"
                              label={`Агент #${selectedMonitoringAgent?.id ?? '-'}`}
                              variant="outlined"
                            />
                            <Chip
                              size="small"
                              label={`Компьютер: ${selectedMonitoringAgent?.computerId ?? '-'}`}
                              variant="outlined"
                            />
                            <Chip
                              size="small"
                              color={String(selectedMonitoringAgent?.status || '').toLowerCase().includes('online') || String(selectedMonitoringAgent?.status || '').toLowerCase().includes('active') ? 'success' : 'warning'}
                              label={getStatusLabel(selectedMonitoringAgent?.status)}
                            />
                            <Chip
                              size="small"
                              variant="outlined"
                              color={selectedMonitoringAgent?.desiredVersion ? 'warning' : 'default'}
                              label={
                                selectedMonitoringAgent?.desiredVersion
                                  ? `Целевая версия: ${selectedMonitoringAgent.desiredVersion}`
                                  : 'Целевая версия не задана'
                              }
                            />
                            <Chip
                              size="small"
                              color={agentPolicyForm.adminBlocked ? 'error' : 'success'}
                              label={agentPolicyForm.adminBlocked ? 'ЗАБЛОКИРОВАН АДМИНИСТРАТОРОМ' : 'НЕ ЗАБЛОКИРОВАН'}
                            />
                            {selectedMonitoringAgent?.lastHeartbeat && (
                              <Chip
                                size="small"
                                variant="outlined"
                                label={`Последний сигнал heartbeat: ${new Date(selectedMonitoringAgent.lastHeartbeat).toLocaleTimeString('ru-RU')}`}
                              />
                            )}
                            {selectedMonitoringAgent?.desiredVersionSetAt && (
                              <Chip
                                size="small"
                                variant="outlined"
                                label={`Назначено: ${new Date(selectedMonitoringAgent.desiredVersionSetAt).toLocaleString('ru-RU')}`}
                              />
                            )}
                          </Stack>
                        </Grid>
                      </Grid>
                    </Grid>

                    <Grid item xs={12} lg={7}>
                      <Paper variant="outlined" sx={{ p: 2 }}>
                        <Box display="flex" justifyContent="space-between" alignItems="center" mb={2}>
                          <Typography variant="subtitle1">Политика сбора</Typography>
                          <Stack direction="row" spacing={1}>
                            <Button
                              size="small"
                              variant="outlined"
                              color="warning"
                              onClick={handleResetAgentPolicy}
                              disabled={!selectedMonitoringAgentId || agentPolicySaving || agentControlLoading}
                            >
                              Сбросить политику
                            </Button>
                            <Button
                              size="small"
                              variant="contained"
                              startIcon={<Save />}
                              onClick={handleSaveAgentPolicy}
                              disabled={!selectedMonitoringAgentId || agentPolicySaving}
                            >
                              {agentPolicySaving ? 'Сохранение...' : 'Сохранить политику'}
                            </Button>
                          </Stack>
                        </Box>

                        <Grid container spacing={2}>
                          <Grid item xs={12} md={4}>
                            <TextField
                              fullWidth
                              size="small"
                              type="number"
                              label="Интервал сбора (с)"
                              value={agentPolicyForm.collectionIntervalSec}
                              onChange={(e) => handleAgentPolicyFieldChange('collectionIntervalSec', e.target.value)}
                            />
                          </Grid>
                          <Grid item xs={12} md={4}>
                            <TextField
                              fullWidth
                              size="small"
                              type="number"
                              label="Интервал heartbeat-сигнала (с)"
                              value={agentPolicyForm.heartbeatIntervalSec}
                              onChange={(e) => handleAgentPolicyFieldChange('heartbeatIntervalSec', e.target.value)}
                            />
                          </Grid>
                          <Grid item xs={12} md={4}>
                            <TextField
                              fullWidth
                              size="small"
                              type="number"
                              label="Интервал отправки (с)"
                              value={agentPolicyForm.flushIntervalSec}
                              onChange={(e) => handleAgentPolicyFieldChange('flushIntervalSec', e.target.value)}
                            />
                          </Grid>

                          <Grid item xs={12} md={4}>
                            <TextField
                              fullWidth
                              size="small"
                              type="number"
                              label="Порог бездействия (с)"
                              value={agentPolicyForm.idleThresholdSec}
                              onChange={(e) => handleAgentPolicyFieldChange('idleThresholdSec', e.target.value)}
                            />
                          </Grid>
                          <Grid item xs={12} md={4}>
                            <TextField
                              fullWidth
                              size="small"
                              type="number"
                              label="Интервал опроса браузера (с)"
                              value={agentPolicyForm.browserPollIntervalSec}
                              onChange={(e) => handleAgentPolicyFieldChange('browserPollIntervalSec', e.target.value)}
                            />
                          </Grid>
                          <Grid item xs={12} md={4}>
                            <TextField
                              fullWidth
                              size="small"
                              type="number"
                              label="Лимит снимка процессов"
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
                              label="Порог высокого риска"
                              value={agentPolicyForm.highRiskThreshold}
                              onChange={(e) => handleAgentPolicyFieldChange('highRiskThreshold', e.target.value)}
                            />
                          </Grid>
                          <Grid item xs={12} md={6}>
                            <TextField
                              fullWidth
                              size="small"
                              label="Браузеры (через запятую)"
                              value={agentPolicyForm.browsersCsv}
                              onChange={(e) => handleAgentPolicyFieldChange('browsersCsv', e.target.value)}
                              placeholder="chrome, edge, firefox"
                            />
                          </Grid>

                          <Grid item xs={12}>
                            <TextField
                              fullWidth
                              size="small"
                              label="Причина блокировки"
                              value={agentPolicyForm.blockedReason}
                              onChange={(e) => handleAgentPolicyFieldChange('blockedReason', e.target.value)}
                              helperText="Используется при админ-блокировке; также заполняется автоматически при блокировке/разблокировке."
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
                              label="Собирать процессы"
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
                              label="Собирать посещения браузера"
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
                              label="Собирать активное окно"
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
                              label="Собирать время бездействия"
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
                              label="Автоблокировка при высоком риске"
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
                              label="Флаг админ-блокировки (только чтение)"
                            />
                          </Grid>
                        </Grid>

                        <Divider sx={{ my: 2 }} />

                        <Box display="flex" justifyContent="space-between" alignItems="center" gap={2} flexWrap="wrap">
                          <Typography variant="caption" color="text.secondary">
                            Версия политики: {selectedAgentPolicy?.policyVersion || '-'} | Обновлено:{' '}
                            {selectedAgentPolicy?.updatedAt ? new Date(selectedAgentPolicy.updatedAt).toLocaleString('ru-RU') : '-'}
                          </Typography>
                          <Stack direction="row" spacing={1}>
                            <Button
                              size="small"
                              color="error"
                              variant="outlined"
                              onClick={() => handleAgentBlockAction(true)}
                              disabled={!selectedMonitoringAgentId || agentActionLoading}
                            >
                              Заблокировать ПК
                            </Button>
                            <Button
                              size="small"
                              color="success"
                              variant="outlined"
                              onClick={() => handleAgentBlockAction(false)}
                              disabled={!selectedMonitoringAgentId || agentActionLoading}
                            >
                              Разблокировать ПК
                            </Button>
                          </Stack>
                        </Box>
                      </Paper>
                    </Grid>

                    <Grid item xs={12} lg={5}>
                      <Paper variant="outlined" sx={{ p: 2, mb: 2 }}>
                        <Typography variant="subtitle1" gutterBottom>Версия агента и self-update</Typography>
                        <Grid container spacing={2}>
                          <Grid item xs={12}>
                            <TextField
                              fullWidth
                              size="small"
                              label="Текущая версия"
                              value={selectedMonitoringAgent?.version || '—'}
                              InputProps={{ readOnly: true }}
                            />
                          </Grid>
                          <Grid item xs={12}>
                            <TextField
                              fullWidth
                              size="small"
                              label="Целевая версия"
                              placeholder="например, 2.1.0"
                              value={desiredAgentVersion}
                              onChange={(e) => setDesiredAgentVersion(e.target.value)}
                              helperText="Пустое значение очищает целевую версию без постановки команды."
                            />
                          </Grid>
                          <Grid item xs={12}>
                            <FormControlLabel
                              control={
                                <Switch
                                  checked={enqueueSelfUpdateCommand}
                                  onChange={(e) => setEnqueueSelfUpdateCommand(e.target.checked)}
                                  disabled={!String(desiredAgentVersion || '').trim()}
                                />
                              }
                              label="Сразу поставить команду SELF_UPDATE"
                            />
                          </Grid>
                          <Grid item xs={12}>
                            <Button
                              size="small"
                              variant="contained"
                              startIcon={<Replay />}
                              onClick={handleSaveDesiredAgentVersion}
                              disabled={!selectedMonitoringAgentId || desiredVersionSaving}
                            >
                              {desiredVersionSaving ? 'Сохранение...' : 'Сохранить целевую версию'}
                            </Button>
                          </Grid>

                          <Grid item xs={12}>
                            <Divider sx={{ my: 1 }} />
                            <Typography variant="subtitle2" sx={{ mb: 1 }}>
                              Rollout обновлений (canary / staged / auto-rollback)
                            </Typography>
                          </Grid>

                          <Grid item xs={12}>
                            <TextField
                              fullWidth
                              size="small"
                              label="Версия для rollout"
                              placeholder="например, 2.1.0"
                              value={rolloutDesiredVersion}
                              onChange={(e) => setRolloutDesiredVersion(e.target.value)}
                            />
                          </Grid>

                          <Grid item xs={12} sm={6}>
                            <FormControl fullWidth size="small">
                              <InputLabel>Стратегия rollout</InputLabel>
                              <Select
                                label="Стратегия rollout"
                                value={rolloutStrategy}
                                onChange={(e) => setRolloutStrategy(e.target.value)}
                              >
                                <MenuItem value="canary">Canary</MenuItem>
                                <MenuItem value="staged">Staged</MenuItem>
                                <MenuItem value="all">Полный rollout</MenuItem>
                              </Select>
                            </FormControl>
                          </Grid>

                          <Grid item xs={12} sm={6}>
                            {rolloutStrategy === 'canary' ? (
                              <TextField
                                fullWidth
                                size="small"
                                type="number"
                                label="Canary (%)"
                                value={rolloutCanaryPercent}
                                onChange={(e) => setRolloutCanaryPercent(e.target.value)}
                                inputProps={{ min: 1, max: 50 }}
                              />
                            ) : rolloutStrategy === 'staged' ? (
                              <TextField
                                fullWidth
                                size="small"
                                type="number"
                                label="Размер этапа"
                                value={rolloutStageSize}
                                onChange={(e) => setRolloutStageSize(e.target.value)}
                                inputProps={{ min: 1, max: 500 }}
                              />
                            ) : (
                              <TextField
                                fullWidth
                                size="small"
                                label="Параметры стратегии"
                                value="Все агенты в одном этапе"
                                InputProps={{ readOnly: true }}
                              />
                            )}
                          </Grid>

                          <Grid item xs={12}>
                            <FormControlLabel
                              control={
                                <Switch
                                  checked={rolloutOnlineOnly}
                                  onChange={(e) => setRolloutOnlineOnly(e.target.checked)}
                                />
                              }
                              label="Только online/active агенты"
                            />
                          </Grid>

                          <Grid item xs={12}>
                            <Stack direction="row" spacing={1} useFlexGap flexWrap="wrap">
                              <Button
                                size="small"
                                variant="outlined"
                                onClick={handlePlanRollout}
                                disabled={rolloutPlanning || rolloutExecuting}
                              >
                                {rolloutPlanning ? 'Расчет...' : 'Рассчитать план'}
                              </Button>
                              {rolloutStages.length > 0 && (
                                <FormControl size="small" sx={{ minWidth: 190 }}>
                                  <InputLabel>Этап</InputLabel>
                                  <Select
                                    label="Этап"
                                    value={selectedRolloutStage}
                                    onChange={(e) => setSelectedRolloutStage(Number(e.target.value))}
                                  >
                                    {rolloutStages.map((stage) => (
                                      <MenuItem key={stage.stage} value={stage.stage}>
                                        Этап {stage.stage}: {stage.count} агентов
                                      </MenuItem>
                                    ))}
                                  </Select>
                                </FormControl>
                              )}
                            </Stack>
                          </Grid>

                          {rolloutStages.length > 0 && (
                            <Grid item xs={12}>
                              <Stack direction="row" spacing={0.5} useFlexGap flexWrap="wrap">
                                {rolloutStages.map((stage) => (
                                  <Chip
                                    key={stage.stage}
                                    size="small"
                                    variant={Number(stage.stage) === Number(selectedRolloutStage) ? 'filled' : 'outlined'}
                                    color={Number(stage.stage) === Number(selectedRolloutStage) ? 'primary' : 'default'}
                                    label={`Этап ${stage.stage}: ${stage.count}`}
                                  />
                                ))}
                              </Stack>
                            </Grid>
                          )}

                          <Grid item xs={12}>
                            <FormControlLabel
                              control={
                                <Switch
                                  checked={rolloutAutoRollback}
                                  onChange={(e) => setRolloutAutoRollback(e.target.checked)}
                                />
                              }
                              label="Auto-rollback при превышении порогов ошибок"
                            />
                          </Grid>

                          <Grid item xs={12} sm={4}>
                            <TextField
                              fullWidth
                              size="small"
                              type="number"
                              inputProps={{ min: 0.01, max: 1, step: 0.01 }}
                              label="Порог failure rate"
                              value={rolloutFailureRateThreshold}
                              onChange={(e) => setRolloutFailureRateThreshold(e.target.value)}
                              disabled={!rolloutAutoRollback}
                            />
                          </Grid>
                          <Grid item xs={12} sm={4}>
                            <TextField
                              fullWidth
                              size="small"
                              type="number"
                              inputProps={{ min: 0, step: 1 }}
                              label="Макс. failed агентов"
                              value={rolloutMaxFailedAgents}
                              onChange={(e) => setRolloutMaxFailedAgents(e.target.value)}
                              disabled={!rolloutAutoRollback}
                            />
                          </Grid>
                          <Grid item xs={12} sm={4}>
                            <TextField
                              fullWidth
                              size="small"
                              type="number"
                              inputProps={{ min: 0, max: 180, step: 1 }}
                              label="Окно наблюдения (сек)"
                              value={rolloutObservationSeconds}
                              onChange={(e) => setRolloutObservationSeconds(e.target.value)}
                              disabled={!rolloutAutoRollback}
                            />
                          </Grid>

                          <Grid item xs={12}>
                            <Button
                              size="small"
                              variant="contained"
                              color="warning"
                              onClick={handleExecuteRollout}
                              disabled={rolloutExecuting || rolloutPlanning || rolloutStages.length === 0}
                            >
                              {rolloutExecuting ? 'Выполнение...' : 'Запустить выбранный этап rollout'}
                            </Button>
                          </Grid>

                          {rolloutResult && (
                            <Grid item xs={12}>
                              <Alert
                                severity={rolloutResult?.autoRollback?.rollbackTriggered ? 'warning' : 'success'}
                                sx={{ mb: 0 }}
                              >
                                <Typography variant="body2" fontWeight={600}>
                                  Rollout: успешно {rolloutResult?.succeededCount ?? 0}, ошибок {rolloutResult?.failedCount ?? 0}
                                </Typography>
                                <Typography variant="caption" display="block">
                                  Auto-rollback: {rolloutResult?.autoRollback?.rollbackTriggered ? 'выполнен' : 'не потребовался'}
                                </Typography>
                              </Alert>
                            </Grid>
                          )}
                        </Grid>
                      </Paper>

                      <Paper variant="outlined" sx={{ p: 2, mb: 2 }}>
                        <Typography variant="subtitle1" gutterBottom>Команды администратора</Typography>
                        <Grid container spacing={2}>
                          <Grid item xs={12} sm={6}>
                            <FormControl fullWidth size="small">
                              <InputLabel>Тип команды</InputLabel>
                              <Select
                                label="Тип команды"
                                value={agentCommandForm.type}
                                onChange={(e) => handleAgentCommandFieldChange('type', e.target.value)}
                              >
                                {AGENT_COMMAND_TYPES.map((type) => (
                                  <MenuItem key={type.value} value={type.value}>{type.label}</MenuItem>
                                ))}
                              </Select>
                            </FormControl>
                          </Grid>
                          <Grid item xs={12} sm={6}>
                            <TextField
                              fullWidth
                              size="small"
                              label="Кем запрошено (необязательно)"
                              value={agentCommandForm.requestedBy}
                              onChange={(e) => handleAgentCommandFieldChange('requestedBy', e.target.value)}
                              placeholder="администратор"
                            />
                          </Grid>
                          <Grid item xs={12}>
                            <TextField
                              fullWidth
                              size="small"
                              label="Причина / примечание"
                              value={agentAdminReason}
                              onChange={(e) => setAgentAdminReason(e.target.value)}
                              placeholder="Необязательная причина блокировки/разблокировки"
                            />
                          </Grid>
                          <Grid item xs={12}>
                            <TextField
                              fullWidth
                              size="small"
                              multiline
                              minRows={4}
                              label="JSON-параметры команды"
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
                                {agentCommandSaving ? 'Отправка...' : 'Отправить команду'}
                              </Button>
                              <Button
                                variant="outlined"
                                onClick={() => handleAgentCommandFieldChange('payloadJson', '{}')}
                                disabled={agentCommandSaving}
                              >
                                Сбросить параметры
                              </Button>
                            </Stack>
                          </Grid>
                        </Grid>
                      </Paper>

                      <Paper variant="outlined" sx={{ p: 2 }}>
                        <Box display="flex" justifyContent="space-between" alignItems="center" gap={2} flexWrap="wrap" mb={1.5}>
                          <Box>
                            <Typography variant="subtitle1">
                              История команд ({agentCommandsTotal || agentCommands.length})
                            </Typography>
                            <Typography variant="caption" color="text.secondary">
                              Автообновление: {
                                agentCommandSaving || agentActionLoading || agentCommands.some((command) => isInFlightCommand(command?.status))
                                  ? 'вкл'
                                  : 'ожидание'
                              }
                            </Typography>
                          </Box>
                          <Stack direction="row" spacing={1}>
                            <FormControl size="small" sx={{ minWidth: 140 }}>
                              <InputLabel>Статус</InputLabel>
                              <Select
                                label="Статус"
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
                            <FormControl size="small" sx={{ minWidth: 190 }}>
                              <InputLabel>Тип</InputLabel>
                              <Select
                                label="Тип"
                                value={agentCommandTypeFilter}
                                onChange={(e) => setAgentCommandTypeFilter(e.target.value)}
                              >
                                <MenuItem value="">Все</MenuItem>
                                {AGENT_COMMAND_TYPES.map((option) => (
                                  <MenuItem key={option.value} value={option.value}>
                                    {option.value}
                                  </MenuItem>
                                ))}
                              </Select>
                            </FormControl>
                            <TextField
                              size="small"
                              label="С даты"
                              type="datetime-local"
                              value={agentCommandFromFilter}
                              onChange={(e) => setAgentCommandFromFilter(e.target.value)}
                              InputLabelProps={{ shrink: true }}
                            />
                            <TextField
                              size="small"
                              label="По дату"
                              type="datetime-local"
                              value={agentCommandToFilter}
                              onChange={(e) => setAgentCommandToFilter(e.target.value)}
                              InputLabelProps={{ shrink: true }}
                            />
                            <Button
                              size="small"
                              variant="outlined"
                              startIcon={<Refresh />}
                              onClick={() => selectedMonitoringAgentId && fetchAgentCommands(selectedMonitoringAgentId)}
                              disabled={!selectedMonitoringAgentId || agentCommandsRefreshing}
                            >
                              {agentCommandsRefreshing ? 'Обновление...' : 'Обновить'}
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
                                  <TableCell>Доставка</TableCell>
                                  <TableCell>Жизненный цикл</TableCell>
                                  <TableCell align="right">Действия</TableCell>
                                </TableRow>
                              </TableHead>
                              <TableBody>
                                {agentCommands.length === 0 ? (
                                  <TableRow>
                                    <TableCell colSpan={6} align="center">Команды не найдены</TableCell>
                                  </TableRow>
                                ) : agentCommands.map((command) => (
                                <TableRow key={command.id} hover>
                                  <TableCell>{command.id}</TableCell>
                                  <TableCell>
                                    <Typography variant="body2" fontWeight={600}>
                                      {getCommandTypeLabel(command.type)}
                                    </Typography>
                                    {command.commandKey && (
                                      <Typography variant="caption" color="text.secondary" display="block">
                                        key: {command.commandKey}
                                      </Typography>
                                    )}
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
                                      label={getStatusLabel(command.status)}
                                    />
                                  </TableCell>
                                  <TableCell>
                                    <Typography variant="caption" display="block">
                                      Попытки: {Number(command.deliveryAttempts) || 0}/{Number(command.maxDeliveryAttempts) || 0}
                                    </Typography>
                                    <Typography variant="caption" color="text.secondary" display="block">
                                      Отправка: {command.lastDispatchAt ? new Date(command.lastDispatchAt).toLocaleTimeString('ru-RU') : '-'}
                                    </Typography>
                                    <Typography variant="caption" color="text.secondary" display="block">
                                      Повтор: {command.nextRetryAt ? new Date(command.nextRetryAt).toLocaleTimeString('ru-RU') : '-'}
                                    </Typography>
                                    {command.timeoutAt && (
                                      <Typography variant="caption" color="text.secondary" display="block">
                                        Таймаут: {new Date(command.timeoutAt).toLocaleTimeString('ru-RU')}
                                      </Typography>
                                    )}
                                    {command.deadLetterReason && (
                                      <Typography variant="caption" color="error.main" display="block">
                                        DLQ: {command.deadLetterReason}
                                      </Typography>
                                    )}
                                  </TableCell>
                                  <TableCell>
                                    <Typography variant="caption" display="block">
                                      {command.createdAt ? new Date(command.createdAt).toLocaleString('ru-RU') : '-'}
                                    </Typography>
                                    <Typography variant="caption" color="text.secondary" display="block">
                                      Подтверждение: {command.acknowledgedAt ? new Date(command.acknowledgedAt).toLocaleString('ru-RU') : '-'}
                                    </Typography>
                                  </TableCell>
                                  <TableCell align="right">
                                    {canRetryCommand(command.status) ? (
                                      <Tooltip title="Поставить команду в очередь повторно">
                                        <span>
                                          <Button
                                            size="small"
                                            variant="outlined"
                                            startIcon={<Replay fontSize="small" />}
                                            onClick={() => handleRetryAgentCommand(command)}
                                            disabled={agentActionLoading || agentCommandSaving}
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
                  Конфигурация мониторинга
                </Typography>
                <Grid container spacing={3}>
                  <Grid item xs={12} md={6}>
                    <TextField
                      fullWidth
                      label="Хранение данных (дни)"
                      type="number"
                      value={monitoringSettings.dataRetentionDays}
                      onChange={(e) => setMonitoringSettings({ ...monitoringSettings, dataRetentionDays: e.target.value })}
                    />
                  </Grid>
                  <Grid item xs={12} md={6}>
                    <TextField
                      fullWidth
                      label="Интервал мониторинга (секунды)"
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
                      label="Мониторинг в реальном времени"
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
                      label="Обнаружение аномалий"
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
                      label="Включить белый список"
                    />
                  </Grid>
                </Grid>
                <Box mt={3}>
                  <Button
                    variant="contained"
                    startIcon={<Save />}
                    onClick={() => handleSaveSettings('Мониторинг')}
                    disabled={loading}
                  >
                    Сохранить настройки мониторинга
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
          <Grid item xs={12}>
            <Card>
              <CardContent>
                <Box display="flex" justifyContent="space-between" alignItems="center" gap={2} flexWrap="wrap">
                  <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap">
                    <Typography variant="h6">Списки доступа</Typography>
                    <Chip
                      size="small"
                      color={listSettingsDirty ? 'warning' : 'success'}
                      label={listSettingsDirty ? 'Есть несохраненные изменения' : 'Сохранено'}
                    />
                    {policySyncInfo && (
                      <Chip
                        size="small"
                        color={getPolicySyncSeverity(policySyncInfo)}
                        label={policySyncInfo.failedAgents > 0 ? 'Синхронизация политик частичная' : 'Синхронизация политик успешна'}
                      />
                    )}
                  </Stack>

                  <Stack direction="row" spacing={1} alignItems="center">
                    <FormControlLabel
                      sx={{ mr: 0 }}
                      control={
                        <Switch
                          checked={listSettingsAutoSave}
                          onChange={(e) => setListSettingsAutoSave(e.target.checked)}
                        />
                      }
                      label="Автосохранение"
                    />
                    <Button
                      variant="outlined"
                      startIcon={<Refresh />}
                      onClick={handleSyncPoliciesNow}
                      disabled={loading || listSettingsSaving || policySyncRunning}
                    >
                      {policySyncRunning ? 'Синхронизация...' : 'Синхронизировать политики'}
                    </Button>
                    <Button
                      variant="contained"
                      startIcon={<Save />}
                      onClick={() => handleSaveAccessLists()}
                      disabled={loading || listSettingsSaving || !listSettingsDirty}
                    >
                      {listSettingsSaving ? 'Сохранение...' : 'Сохранить списки доступа'}
                    </Button>
                  </Stack>
                </Box>
              </CardContent>
            </Card>
          </Grid>

          <Grid item xs={12} md={6}>
            <Card>
              <CardContent>
                <Box display="flex" justifyContent="space-between" alignItems="center" mb={2}>
                  <Typography variant="h6">Белый список</Typography>
                  <Button
                    variant="outlined"
                    size="small"
                    startIcon={<Add />}
                    onClick={handleAddWhitelistEntry}
                  >
                    Добавить запись
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
                            placeholder="Название приложения"
                            value={entry.application}
                            onChange={(e) => handleUpdateWhitelistEntry(entry.id, 'application', e.target.value)}
                          />
                        }
                        secondary={
                          <TextField
                            fullWidth
                            size="small"
                            placeholder="Описание"
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
                  <Typography variant="h6">Черный список</Typography>
                  <Button
                    variant="outlined"
                    size="small"
                    startIcon={<Add />}
                    onClick={handleAddBlacklistEntry}
                  >
                    Добавить запись
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
                            placeholder="Название приложения"
                            value={entry.application}
                            onChange={(e) => handleUpdateBlacklistEntry(entry.id, 'application', e.target.value)}
                          />
                        }
                        secondary={
                          <TextField
                            fullWidth
                            size="small"
                            placeholder="Описание"
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

      {tabValue === 5 && (
        <Card>
          <CardContent>
            <Box display="flex" justifyContent="space-between" alignItems="center" gap={2} flexWrap="wrap" mb={2}>
              <Box>
                <Typography variant="h6">Журнал аудита</Typography>
                <Typography variant="body2" color="text.secondary">
                  Фиксация административных действий с фильтрами, поиском и экспортом CSV.
                </Typography>
              </Box>
              <Stack direction="row" spacing={1}>
                <Button
                  variant="outlined"
                  startIcon={<Refresh />}
                  onClick={() => fetchAuditEvents()}
                  disabled={auditLoading}
                >
                  {auditLoading ? 'Обновление...' : 'Обновить'}
                </Button>
                <Button
                  variant="contained"
                  startIcon={<FileDownload />}
                  onClick={handleExportAuditCsv}
                  disabled={auditExporting}
                >
                  {auditExporting ? 'Экспорт...' : 'Экспорт CSV'}
                </Button>
              </Stack>
            </Box>

            {auditError && (
              <Alert severity="warning" sx={{ mb: 2 }}>
                {auditError}
              </Alert>
            )}

            <Grid container spacing={2} sx={{ mb: 2 }}>
              <Grid item xs={12} md={2}>
                <FormControl fullWidth size="small">
                  <InputLabel>Действие</InputLabel>
                  <Select
                    label="Действие"
                    value={auditActionFilter}
                    onChange={(e) => setAuditActionFilter(e.target.value)}
                  >
                    <MenuItem value="">Все</MenuItem>
                    {auditActionOptions.map((value) => (
                      <MenuItem key={value} value={value}>{value}</MenuItem>
                    ))}
                  </Select>
                </FormControl>
              </Grid>
              <Grid item xs={12} md={2}>
                <FormControl fullWidth size="small">
                  <InputLabel>Актор</InputLabel>
                  <Select
                    label="Актор"
                    value={auditActorFilter}
                    onChange={(e) => setAuditActorFilter(e.target.value)}
                  >
                    <MenuItem value="">Все</MenuItem>
                    {auditActorOptions.map((value) => (
                      <MenuItem key={value} value={value}>{value}</MenuItem>
                    ))}
                  </Select>
                </FormControl>
              </Grid>
              <Grid item xs={12} md={3}>
                <TextField
                  fullWidth
                  size="small"
                  label="Поиск"
                  value={auditSearchFilter}
                  onChange={(e) => setAuditSearchFilter(e.target.value)}
                  placeholder="action, actor, target, details"
                />
              </Grid>
              <Grid item xs={12} md={2}>
                <TextField
                  fullWidth
                  size="small"
                  type="datetime-local"
                  label="С"
                  value={auditFromFilter}
                  onChange={(e) => setAuditFromFilter(e.target.value)}
                  InputLabelProps={{ shrink: true }}
                />
              </Grid>
              <Grid item xs={12} md={2}>
                <TextField
                  fullWidth
                  size="small"
                  type="datetime-local"
                  label="По"
                  value={auditToFilter}
                  onChange={(e) => setAuditToFilter(e.target.value)}
                  InputLabelProps={{ shrink: true }}
                />
              </Grid>
              <Grid item xs={12} md={1} display="flex" justifyContent="flex-end">
                <Stack direction="row" spacing={1}>
                  <Button variant="outlined" size="small" onClick={handleResetAuditFilters}>Сброс</Button>
                  <Button variant="contained" size="small" onClick={handleApplyAuditFilters}>Применить</Button>
                </Stack>
              </Grid>
            </Grid>

            {auditLoading && <LinearProgress sx={{ mb: 2, borderRadius: 999 }} />}

            <TableContainer component={Paper} variant="outlined">
              <Table size="small">
                <TableHead>
                  <TableRow>
                    <TableCell>Время</TableCell>
                    <TableCell>Актор</TableCell>
                    <TableCell>Действие</TableCell>
                    <TableCell>Цель</TableCell>
                    <TableCell>Статус</TableCell>
                    <TableCell>Код</TableCell>
                    <TableCell>Детали</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {auditEvents.length === 0 ? (
                    <TableRow>
                      <TableCell colSpan={7} align="center">События аудита не найдены</TableCell>
                    </TableRow>
                  ) : (
                    auditEvents.map((event) => (
                      <TableRow key={event.id} hover>
                        <TableCell>{event.createdAt ? new Date(event.createdAt).toLocaleString('ru-RU') : '—'}</TableCell>
                        <TableCell>{event.actor || '—'}</TableCell>
                        <TableCell>
                          <Typography variant="body2" fontWeight={600}>{event.action || '—'}</Typography>
                        </TableCell>
                        <TableCell>{`${event.targetType || '—'}:${event.targetId || '—'}`}</TableCell>
                        <TableCell>
                          <Chip
                            size="small"
                            color={event.success ? 'success' : 'error'}
                            label={event.success ? 'Успех' : 'Ошибка'}
                          />
                        </TableCell>
                        <TableCell>{event.statusCode || '—'}</TableCell>
                        <TableCell>
                          <Tooltip title={event.detailsJson || '—'}>
                            <Typography variant="caption">{formatAuditDetailsPreview(event.detailsJson)}</Typography>
                          </Tooltip>
                        </TableCell>
                      </TableRow>
                    ))
                  )}
                </TableBody>
              </Table>
            </TableContainer>

            <Box mt={2} display="flex" justifyContent="space-between" alignItems="center" gap={2} flexWrap="wrap">
              <Typography variant="body2" color="text.secondary">
                Всего записей: {auditTotalCount}
              </Typography>
              <Stack direction="row" spacing={1} alignItems="center">
                <FormControl size="small" sx={{ minWidth: 120 }}>
                  <InputLabel>На странице</InputLabel>
                  <Select
                    label="На странице"
                    value={auditPageSize}
                    onChange={(e) => {
                      const nextSize = Number(e.target.value) || 25;
                      setAuditPageSize(nextSize);
                      setAuditPage(1);
                    }}
                  >
                    {[25, 50, 100, 200].map((size) => (
                      <MenuItem key={size} value={size}>{size}</MenuItem>
                    ))}
                  </Select>
                </FormControl>
                <Button
                  size="small"
                  variant="outlined"
                  onClick={() => setAuditPage((prev) => Math.max(1, prev - 1))}
                  disabled={auditPage <= 1}
                >
                  Назад
                </Button>
                <Chip size="small" variant="outlined" label={`Страница ${auditPage} из ${auditTotalPages}`} />
                <Button
                  size="small"
                  variant="outlined"
                  onClick={() => setAuditPage((prev) => Math.min(auditTotalPages, prev + 1))}
                  disabled={auditPage >= auditTotalPages}
                >
                  Вперёд
                </Button>
              </Stack>
            </Box>
          </CardContent>
        </Card>
      )}

      <Dialog
        open={alertRuleDialogOpen}
        onClose={() => setAlertRuleDialogOpen(false)}
        fullWidth
        maxWidth="md"
      >
        <DialogTitle>{editingAlertRuleId ? 'Редактировать правило тревоги' : 'Создать правило тревоги'}</DialogTitle>
        <DialogContent>
          <Grid container spacing={2} sx={{ mt: 0.5 }}>
            <Grid item xs={12} md={8}>
              <TextField
                fullWidth
                label="Название правила"
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
                label="Включено"
              />
            </Grid>
            <Grid item xs={12} md={4}>
              <FormControl fullWidth>
                <InputLabel>Метрика</InputLabel>
                <Select
                  label="Метрика"
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
                <InputLabel>Оператор</InputLabel>
                <Select
                  label="Оператор"
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
                label="Порог"
                type="number"
                value={alertRuleForm.threshold}
                onChange={(e) => handleAlertRuleFieldChange('threshold', e.target.value)}
              />
            </Grid>
            <Grid item xs={12} md={4}>
              <FormControl fullWidth>
                <InputLabel>Серьезность</InputLabel>
                <Select
                  label="Серьезность"
                  value={alertRuleForm.severity}
                  onChange={(e) => handleAlertRuleFieldChange('severity', e.target.value)}
                >
                  {alertRuleSeverities.map((severity) => (
                    <MenuItem key={severity} value={severity}>
                      {String(severity).toLowerCase() === 'critical' || String(severity).toLowerCase() === 'high'
                        ? 'Высокая'
                        : String(severity).toLowerCase() === 'medium'
                          ? 'Средняя'
                          : String(severity).toLowerCase() === 'low'
                            ? 'Низкая'
                            : String(severity)}
                    </MenuItem>
                  ))}
                </Select>
              </FormControl>
            </Grid>
            <Grid item xs={12} md={4}>
              <TextField
                fullWidth
                label="Окно (минуты)"
                type="number"
                value={alertRuleForm.windowMinutes}
                onChange={(e) => handleAlertRuleFieldChange('windowMinutes', e.target.value)}
              />
            </Grid>
            <Grid item xs={12} md={4}>
              <TextField
                fullWidth
                label="Пауза (минуты)"
                type="number"
                value={alertRuleForm.cooldownMinutes}
                onChange={(e) => handleAlertRuleFieldChange('cooldownMinutes', e.target.value)}
              />
            </Grid>
            <Grid item xs={12} md={4}>
              <TextField
                fullWidth
                label="Тип активности (необязательно)"
                placeholder="FILE_ACCESS"
                value={alertRuleForm.activityType}
                onChange={(e) => handleAlertRuleFieldChange('activityType', e.target.value)}
              />
            </Grid>
            <Grid item xs={12} md={4}>
              <TextField
                fullWidth
                label="ID пользователя (необязательно)"
                type="number"
                value={alertRuleForm.userId}
                onChange={(e) => handleAlertRuleFieldChange('userId', e.target.value)}
              />
            </Grid>
            <Grid item xs={12} md={6}>
              <TextField
                fullWidth
                label="ID компьютера (необязательно)"
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
                label="В приложении"
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
                label="Почта"
              />
            </Grid>
          </Grid>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setAlertRuleDialogOpen(false)} disabled={alertRuleSaving}>
            Отмена
          </Button>
          <Button
            onClick={handleSaveAlertRule}
            variant="contained"
            startIcon={<Save />}
            disabled={alertRuleSaving}
          >
            {alertRuleSaving ? 'Сохранение...' : editingAlertRuleId ? 'Сохранить правило' : 'Создать правило'}
          </Button>
        </DialogActions>
      </Dialog>

      {/* Confirmation Dialog */}
      <Dialog open={confirmDialogOpen} onClose={() => setConfirmDialogOpen(false)}>
        <DialogTitle>Подтвердите действие</DialogTitle>
        <DialogContent>
          <Typography>{confirmAction?.message}</Typography>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setConfirmDialogOpen(false)}>Отмена</Button>
          <Button onClick={handleConfirmAction} variant="contained" color="primary">
            Подтвердить
          </Button>
        </DialogActions>
      </Dialog>
    </Box>
  );
};

export default Settings;
