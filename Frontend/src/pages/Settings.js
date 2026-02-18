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
  Divider,
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
  Tabs,
  Tab,
  Select,
  MenuItem,
  FormControl,
  InputLabel
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
import axios from 'axios';

const Settings = () => {
  const { user } = useAuth();
  const { addNotification } = useNotifications();
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(null);
  const [success, setSuccess] = useState(null);
  const [tabValue, setTabValue] = useState(0);
  const [confirmDialogOpen, setConfirmDialogOpen] = useState(false);
  const [confirmAction, setConfirmAction] = useState(null);

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

  const fetchSettings = async () => {
    try {
      setLoading(true);
      // In a real application, this would fetch settings from the API
      // For now, we're using default values
    } catch (err) {
      setError('Failed to load settings');
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
      
      // In a real application, this would save settings via API
      console.log(`Saving ${category} settings`);
      
      setSuccess(`${category} settings saved successfully`);
      addNotification({
        type: 'success',
        message: `${category} settings have been updated`,
        timestamp: new Date().toISOString()
      });
      
      setTimeout(() => setSuccess(null), 3000);
    } catch (err) {
      setError('Failed to save settings');
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

  const handleConfirmAction = () => {
    if (confirmAction?.type === 'delete_whitelist') {
      setWhitelistEntries(whitelistEntries.filter(entry => entry.id !== confirmAction.id));
    } else if (confirmAction?.type === 'delete_blacklist') {
      setBlacklistEntries(blacklistEntries.filter(entry => entry.id !== confirmAction.id));
    }
    setConfirmDialogOpen(false);
    setConfirmAction(null);
  };

  const handleSystemRestart = () => {
    setConfirmAction({
      type: 'restart_system',
      message: 'Are you sure you want to restart the monitoring system? This will temporarily interrupt monitoring.'
    });
    setConfirmDialogOpen(true);
  };

  return (
    <Box>
      <Box display="flex" justifyContent="space-between" alignItems="center" mb={3}>
        <Typography variant="h4">System Settings</Typography>
        <Button
          variant="outlined"
          startIcon={<Refresh />}
          onClick={handleSystemRestart}
        >
          Restart System
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