import React, { useEffect, useMemo, useRef, useState } from 'react';
import {
  alpha,
} from '@mui/material/styles';
import {
  Box,
  Drawer,
  AppBar,
  Toolbar,
  List,
  Typography,
  Divider,
  IconButton,
  ListItem,
  ListItemButton,
  ListItemIcon,
  ListItemText,
  Avatar,
  Menu,
  MenuItem,
  Badge,
  Tooltip,
  Chip,
  Stack,
  Button,
  CircularProgress,
  Chip as MuiChip,
} from '@mui/material';
import {
  Menu as MenuIcon,
  Dashboard,
  People,
  Memory,
  Assessment,
  Settings,
  Notifications,
  AccountCircle,
  Logout,
  Security,
  BarChart,
  Refresh,
  CheckCircleOutline,
  DeleteOutline,
  AccessTime,
  DarkMode,
  LightMode,
} from '@mui/icons-material';
import { useNavigate, useLocation, Outlet } from 'react-router-dom';
import { useAuth } from '../contexts/AuthContext';
import { useNotifications } from '../contexts/NotificationContext';
import { useThemeMode } from '../contexts/ThemeModeContext';
import { activityAPI, liveAPI } from '../services/api';
import AlertDetailsDialog from './AlertDetailsDialog';

const drawerWidth = 272;

const menuItems = [
  { text: 'Панель мониторинга', icon: <Dashboard />, path: '/dashboard', subtitle: 'Оперативная активность' },
  { text: 'Агенты', icon: <Memory />, path: '/agents', subtitle: 'Устройства и управление' },
  { text: 'Пользователи', icon: <People />, path: '/users', subtitle: 'Доступ и учетные записи' },
  { text: 'Отчеты', icon: <Assessment />, path: '/reports', subtitle: 'Экспорт и тренды' },
  { text: 'Аналитика', icon: <BarChart />, path: '/analytics', subtitle: 'Подробные метрики' },
  { text: 'Настройки', icon: <Settings />, path: '/settings', subtitle: 'Конфигурация системы' },
];

const pageTitles = {
  '/dashboard': { title: 'Панель мониторинга', subtitle: 'Текущая активность, тревоги и тренды' },
  '/agents': { title: 'Агенты', subtitle: 'Инвентаризация, возможности и история команд' },
  '/users': { title: 'Пользователи', subtitle: 'Учетные записи, роли и активные рабочие станции' },
  '/reports': { title: 'Отчеты', subtitle: 'Сформированные отчеты и экспорт' },
  '/analytics': { title: 'Аналитика', subtitle: 'Разбор поведения и активности' },
  '/settings': { title: 'Системные настройки', subtitle: 'Безопасность, уведомления и мониторинг' },
};

const formatDateTime = (value) => {
  if (!value) return '—';

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return String(value);

  return new Intl.DateTimeFormat('ru-RU', {
    day: '2-digit',
    month: 'short',
    hour: '2-digit',
    minute: '2-digit',
  }).format(date);
};

const getNotificationText = (notification) => {
  if (!notification) return { title: 'Уведомление', body: '' };

  return {
    title: notification.title || notification.type || 'Уведомление',
    body: notification.message || notification.description || notification.text || 'Нет подробностей',
  };
};

const getNotificationSeverity = (notification) => {
  const haystack = `${notification?.type || ''} ${notification?.title || ''} ${notification?.message || ''}`.toLowerCase();
  if (/critical|malware|unauthor|exfil|high/.test(haystack)) return 'high';
  if (/anomaly|blocked|warning|suspicious/.test(haystack)) return 'medium';
  return 'low';
};

const SNOOZE_STORAGE_KEY = 'notification_snooze_until';

const Layout = () => {
  const { user, logout } = useAuth();
  const { mode: themeMode, toggleMode } = useThemeMode();
  const {
    notifications,
    unreadCount,
    loading: notificationsLoading,
    markAsRead,
    markAllAsRead,
    deleteNotification,
    applyLiveSnapshot,
    fetchNotifications,
  } = useNotifications();

  const navigate = useNavigate();
  const location = useLocation();

  const [mobileOpen, setMobileOpen] = useState(false);
  const [profileAnchorEl, setProfileAnchorEl] = useState(null);
  const [notificationAnchorEl, setNotificationAnchorEl] = useState(null);
  const [notificationFilter, setNotificationFilter] = useState('all');
  const [snoozedUntilMap, setSnoozedUntilMap] = useState({});
  const [liveConnected, setLiveConnected] = useState(false);
  const [selectedAlert, setSelectedAlert] = useState(null);
  const [selectedActivity, setSelectedActivity] = useState(null);
  const [alertDetailsOpen, setAlertDetailsOpen] = useState(false);
  const lastUnreadCountRef = useRef(unreadCount);

  useEffect(() => {
    lastUnreadCountRef.current = unreadCount;
  }, [unreadCount]);

  useEffect(() => {
    try {
      const raw = localStorage.getItem(SNOOZE_STORAGE_KEY);
      if (!raw) return;
      const parsed = JSON.parse(raw);
      if (parsed && typeof parsed === 'object') {
        setSnoozedUntilMap(parsed);
      }
    } catch {
      // ignore bad local storage payloads
    }
  }, []);

  const persistSnoozeMap = (nextMap) => {
    setSnoozedUntilMap(nextMap);
    localStorage.setItem(SNOOZE_STORAGE_KEY, JSON.stringify(nextMap));
  };

  const routeMeta = pageTitles[location.pathname] || {
    title: 'Мониторинг активности',
    subtitle: 'Защищенное рабочее пространство',
  };

  const isProfileMenuOpen = Boolean(profileAnchorEl);
  const isNotificationMenuOpen = Boolean(notificationAnchorEl);

  const handleDrawerToggle = () => {
    setMobileOpen((prev) => !prev);
  };

  const handleProfileMenuOpen = (event) => {
    setProfileAnchorEl(event.currentTarget);
  };

  const handleProfileMenuClose = () => {
    setProfileAnchorEl(null);
  };

  const handleNotificationMenuOpen = (event) => {
    setNotificationAnchorEl(event.currentTarget);
  };

  const handleNotificationMenuClose = () => {
    setNotificationAnchorEl(null);
  };

  const handleLogout = async () => {
    handleProfileMenuClose();
    await logout();
    navigate('/login');
  };

  const handleNotificationClick = async (notification) => {
    if (!notification?.isRead) {
      await markAsRead(notification.id);
    }

    const mappedAlert = {
      id: notification?.id,
      type: notification?.type || notification?.title,
      title: notification?.title,
      description: notification?.message || notification?.description || notification?.text,
      timestamp: notification?.timestamp,
      sentAt: notification?.sentAt,
      channel: notification?.channel,
      source: notification?.source,
      activityId: notification?.activityId ?? notification?.activity_id,
      isRead: notification?.isRead ?? notification?.is_read,
      acknowledged: notification?.acknowledged,
      severity: getNotificationSeverity(notification) === 'high'
        ? 'Высокий'
        : getNotificationSeverity(notification) === 'medium'
          ? 'Средний'
          : 'Низкий',
    };
    setSelectedAlert(mappedAlert);
    setSelectedActivity(null);
    setAlertDetailsOpen(true);

    const activityId = mappedAlert.activityId;
    if (activityId == null) return;

    try {
      const response = await activityAPI.getActivities({
        page: 1,
        pageSize: 25,
        activityId,
        id: activityId,
      });
      const items = response?.items || response?.activities || (Array.isArray(response) ? response : []);
      const matched = (Array.isArray(items) ? items : []).find((item) => Number(item?.id) === Number(activityId));
      if (!matched) return;

      setSelectedActivity({
        id: matched?.id,
        timestamp: matched?.timestamp,
        activityType: matched?.activityType ?? matched?.activity_type,
        computerId: matched?.computerId ?? matched?.computer_id,
        processName: matched?.processName ?? matched?.process_name,
        riskScore: Number(matched?.riskScore ?? matched?.risk_score ?? 0),
        url: matched?.url ?? '',
        details: matched?.details ?? '',
      });
    } catch {
      setSelectedActivity(null);
    }
  };

  const handleSnoozeNotification = (notificationId, minutes = 15) => {
    const until = new Date(Date.now() + minutes * 60 * 1000).toISOString();
    persistSnoozeMap({
      ...snoozedUntilMap,
      [notificationId]: until,
    });
  };

  const handleDeleteNotification = async (event, notificationId) => {
    event.stopPropagation();
    await deleteNotification(notificationId);
  };

  const handleSnoozeClick = (event, notificationId) => {
    event.stopPropagation();
    handleSnoozeNotification(notificationId);
  };

  const visibleNotifications = useMemo(() => {
    const now = Date.now();
    const activeSnooze = { ...snoozedUntilMap };

    Object.entries(activeSnooze).forEach(([id, until]) => {
      const ts = Date.parse(until);
      if (!Number.isFinite(ts) || ts <= now) {
        delete activeSnooze[id];
      }
    });

    if (Object.keys(activeSnooze).length !== Object.keys(snoozedUntilMap).length) {
      localStorage.setItem(SNOOZE_STORAGE_KEY, JSON.stringify(activeSnooze));
    }

    return notifications.filter((notification) => {
      const snoozedUntil = activeSnooze[notification.id];
      if (snoozedUntil && Date.parse(snoozedUntil) > now) {
        return false;
      }

      if (notificationFilter === 'unread') {
        return !(notification?.isRead ?? notification?.is_read);
      }

      if (notificationFilter === 'high') {
        return getNotificationSeverity(notification) === 'high';
      }

      return true;
    });
  }, [notifications, notificationFilter, snoozedUntilMap]);

  useEffect(() => {
    const streamUrl = liveAPI.getStreamUrl();
    if (!streamUrl.includes('access_token=')) {
      return undefined;
    }

    const eventSource = new EventSource(streamUrl);

    eventSource.onopen = () => {
      setLiveConnected(true);
    };

    eventSource.onerror = () => {
      setLiveConnected(false);
    };

    const handleSnapshot = (event) => {
      try {
        const payload = JSON.parse(event.data);
        applyLiveSnapshot(payload);

        const nextUnread = payload?.notifications?.unreadCount;
        if (typeof nextUnread === 'number' && nextUnread > lastUnreadCountRef.current) {
          fetchNotifications();
        }

        window.dispatchEvent(new CustomEvent('ams:live-update', { detail: payload }));
      } catch (error) {
        console.error('Failed to parse live snapshot', error);
      }
    };

    eventSource.addEventListener('snapshot', handleSnapshot);

    return () => {
      eventSource.removeEventListener('snapshot', handleSnapshot);
      eventSource.close();
      setLiveConnected(false);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const drawer = (
    <Box sx={{ height: '100%', display: 'flex', flexDirection: 'column', p: 2 }}>
      <Box
        sx={(theme) => ({
          p: 2,
          mb: 2,
          borderRadius: 3,
          border: `1px solid ${alpha(theme.palette.primary.main, 0.12)}`,
          background: `linear-gradient(145deg, ${alpha(theme.palette.primary.light, 0.14)} 0%, ${alpha(theme.palette.secondary.light, 0.08)} 100%)`,
        })}
      >
        <Stack direction="row" spacing={1.5} alignItems="center">
          <Avatar
            variant="rounded"
            sx={(theme) => ({
              width: 44,
              height: 44,
              borderRadius: 2,
              bgcolor: alpha(theme.palette.primary.main, 0.12),
              color: theme.palette.primary.dark,
            })}
          >
            <Security />
          </Avatar>
          <Box>
            <Typography variant="subtitle1" sx={{ fontWeight: 700 }}>
              Монитор активности
            </Typography>
            <Typography variant="caption" color="text.secondary">
              Консоль безопасности
            </Typography>
          </Box>
        </Stack>
      </Box>

      <Typography
        variant="overline"
        color="text.secondary"
        sx={{ px: 1.5, letterSpacing: '0.08em', fontWeight: 700 }}
      >
        Навигация
      </Typography>

      <List sx={{ mt: 0.5, px: 0.5 }}>
        {menuItems.map((item) => {
          const selected = location.pathname === item.path;
          return (
            <ListItem key={item.text} disablePadding sx={{ mb: 0.5 }}>
              <ListItemButton
                selected={selected}
                onClick={() => {
                  navigate(item.path);
                  setMobileOpen(false);
                }}
                sx={(theme) => ({
                  borderRadius: 2.5,
                  alignItems: 'flex-start',
                  px: 1.25,
                  py: 1.1,
                  transition: 'all 160ms ease',
                  '&.Mui-selected': {
                    bgcolor: alpha(theme.palette.primary.main, 0.10),
                    border: `1px solid ${alpha(theme.palette.primary.main, 0.16)}`,
                  },
                  '&.Mui-selected:hover': {
                    bgcolor: alpha(theme.palette.primary.main, 0.14),
                  },
                })}
              >
                <ListItemIcon sx={{ minWidth: 38, mt: 0.15 }}>
                  {item.icon}
                </ListItemIcon>
                <ListItemText
                  primary={item.text}
                  secondary={item.subtitle}
                  primaryTypographyProps={{ fontWeight: 600 }}
                  secondaryTypographyProps={{ variant: 'caption', sx: { lineHeight: 1.35 } }}
                />
              </ListItemButton>
            </ListItem>
          );
        })}
      </List>

      <Box sx={{ flexGrow: 1 }} />

      <Box
        sx={(theme) => ({
          p: 1.5,
          borderRadius: 2.5,
          border: `1px solid ${alpha(theme.palette.success.main, 0.16)}`,
          backgroundColor: alpha(theme.palette.success.main, 0.06),
        })}
      >
        <Stack direction="row" spacing={1} alignItems="center" mb={1}>
          <CheckCircleOutline sx={{ color: 'success.main', fontSize: 18 }} />
          <Typography variant="body2" sx={{ fontWeight: 600 }}>
            Состояние системы: онлайн
          </Typography>
        </Stack>
        <Typography variant="caption" color="text.secondary" display="block">
          Шлюз и основные сервисы доступны.
        </Typography>
        <Chip
          size="small"
          label={(user?.role || 'user').toUpperCase()}
          color="primary"
          sx={{ mt: 1.25 }}
        />
        <Chip
          size="small"
          label={liveConnected ? 'Онлайн' : 'Опрос'}
          color={liveConnected ? 'success' : 'default'}
          variant={liveConnected ? 'filled' : 'outlined'}
          sx={{ mt: 1.25, ml: 1 }}
        />
      </Box>
    </Box>
  );

  return (
    <Box sx={{ display: 'flex', minHeight: '100vh' }}>
      <AppBar
        position="fixed"
        elevation={0}
        sx={(theme) => ({
          width: { sm: `calc(100% - ${drawerWidth}px)` },
          ml: { sm: `${drawerWidth}px` },
          backgroundColor: alpha(theme.palette.background.paper, theme.palette.mode === 'dark' ? 0.78 : 0.72),
          color: 'text.primary',
          boxShadow: 'none',
        })}
      >
        <Toolbar sx={{ minHeight: 74, px: { xs: 2, md: 3 } }}>
          <IconButton
            color="inherit"
            aria-label="открыть навигацию"
            edge="start"
            onClick={handleDrawerToggle}
            sx={{ mr: 1.5, display: { sm: 'none' } }}
          >
            <MenuIcon />
          </IconButton>

          <Box sx={{ flexGrow: 1, minWidth: 0 }}>
            <Typography variant="h6" noWrap>
              {routeMeta.title}
            </Typography>
            <Typography variant="body2" color="text.secondary" noWrap sx={{ mt: 0.2 }}>
              {routeMeta.subtitle}
            </Typography>
          </Box>

          <Stack direction="row" spacing={1} alignItems="center">
            <Tooltip title={themeMode === 'dark' ? 'Светлая тема (бело-синяя)' : 'Темная тема (черно-желтая)'}>
              <IconButton color="inherit" onClick={toggleMode}>
                {themeMode === 'dark' ? <LightMode fontSize="small" /> : <DarkMode fontSize="small" />}
              </IconButton>
            </Tooltip>

            <Tooltip title="Обновить уведомления">
              <span>
                <IconButton
                  color="inherit"
                  onClick={fetchNotifications}
                  disabled={notificationsLoading}
                  size="small"
                >
                  {notificationsLoading ? <CircularProgress size={18} /> : <Refresh fontSize="small" />}
                </IconButton>
              </span>
            </Tooltip>

            <Tooltip title="Уведомления">
              <IconButton color="inherit" onClick={handleNotificationMenuOpen}>
                <Badge
                  badgeContent={unreadCount}
                  color="error"
                  max={99}
                  overlap="circular"
                >
                  <Notifications />
                </Badge>
              </IconButton>
            </Tooltip>

            <Button
              onClick={handleProfileMenuOpen}
              color="inherit"
              sx={(theme) => ({
                borderRadius: 999,
                pl: 0.5,
                pr: 1.25,
                py: 0.4,
                border: `1px solid ${alpha(theme.palette.primary.main, 0.12)}`,
                backgroundColor: alpha(theme.palette.background.paper, theme.palette.mode === 'dark' ? 0.60 : 0.60),
                display: { xs: 'none', sm: 'inline-flex' },
              })}
              startIcon={
                <Avatar sx={{ width: 30, height: 30, bgcolor: 'primary.main', fontSize: 14 }}>
                  {user?.username?.charAt(0)?.toUpperCase() || <AccountCircle fontSize="small" />}
                </Avatar>
              }
            >
              <Box textAlign="left">
                <Typography variant="body2" sx={{ fontWeight: 600, lineHeight: 1.1 }}>
                  {user?.username || 'Пользователь'}
                </Typography>
                <Typography variant="caption" color="text.secondary" sx={{ lineHeight: 1.1 }}>
                  {user?.role || 'оператор'}
                </Typography>
              </Box>
            </Button>

            <IconButton
              color="inherit"
              onClick={handleProfileMenuOpen}
              sx={{ display: { xs: 'inline-flex', sm: 'none' } }}
            >
              <Avatar sx={{ width: 30, height: 30, bgcolor: 'primary.main' }}>
                {user?.username?.charAt(0)?.toUpperCase() || <AccountCircle fontSize="small" />}
              </Avatar>
            </IconButton>
          </Stack>
        </Toolbar>
      </AppBar>

      <Box component="nav" sx={{ width: { sm: drawerWidth }, flexShrink: { sm: 0 } }}>
        <Drawer
          variant="temporary"
          open={mobileOpen}
          onClose={handleDrawerToggle}
          ModalProps={{ keepMounted: true }}
          sx={{
            display: { xs: 'block', sm: 'none' },
            '& .MuiDrawer-paper': { boxSizing: 'border-box', width: drawerWidth },
          }}
        >
          {drawer}
        </Drawer>

        <Drawer
          variant="permanent"
          open
          sx={{
            display: { xs: 'none', sm: 'block' },
            '& .MuiDrawer-paper': { boxSizing: 'border-box', width: drawerWidth },
          }}
        >
          {drawer}
        </Drawer>
      </Box>

      <Box
        component="main"
        sx={{
          flexGrow: 1,
          width: { sm: `calc(100% - ${drawerWidth}px)` },
          minHeight: '100vh',
          px: { xs: 2, md: 3 },
          pb: 4,
        }}
      >
        <Toolbar sx={{ minHeight: 74 }} />
        <Box className="mui-page-shell" sx={{ maxWidth: 1480, mx: 'auto' }}>
          <Outlet />
        </Box>
      </Box>

      <Menu
        anchorEl={notificationAnchorEl}
        open={isNotificationMenuOpen}
        onClose={handleNotificationMenuClose}
        PaperProps={{
          sx: (theme) => ({
            width: 380,
            maxWidth: 'calc(100vw - 24px)',
            mt: 1.2,
            p: 0.5,
            borderRadius: 3,
            border: `1px solid ${alpha(theme.palette.primary.main, 0.12)}`,
            backgroundColor: alpha(theme.palette.background.paper, theme.palette.mode === 'dark' ? 0.92 : 0.92),
            backdropFilter: 'blur(18px)',
          }),
        }}
        transformOrigin={{ horizontal: 'right', vertical: 'top' }}
        anchorOrigin={{ horizontal: 'right', vertical: 'bottom' }}
      >
        <Box sx={{ px: 1.25, py: 1 }}>
          <Stack direction="row" justifyContent="space-between" alignItems="center">
            <Box>
              <Typography variant="subtitle1" sx={{ fontWeight: 700 }}>
                Уведомления
              </Typography>
              <Typography variant="caption" color="text.secondary">
                {unreadCount > 0 ? `${unreadCount} непрочитанных` : 'Новых уведомлений нет'}
              </Typography>
            </Box>
            <Button size="small" onClick={markAllAsRead} disabled={!unreadCount}>
              Отметить все прочитанными
            </Button>
          </Stack>
          <Stack direction="row" spacing={0.75} sx={{ mt: 1.25 }}>
            {[
              { key: 'all', label: 'Все' },
              { key: 'unread', label: 'Непрочитанные' },
              { key: 'high', label: 'Высокий риск' },
            ].map((item) => (
              <MuiChip
                key={item.key}
                size="small"
                label={item.label}
                clickable
                color={notificationFilter === item.key ? 'primary' : 'default'}
                variant={notificationFilter === item.key ? 'filled' : 'outlined'}
                onClick={() => setNotificationFilter(item.key)}
              />
            ))}
          </Stack>
        </Box>
        <Divider sx={{ mx: 1, mb: 0.5 }} />

        {notificationsLoading && notifications.length === 0 ? (
          <Box sx={{ p: 2.5, textAlign: 'center' }}>
            <CircularProgress size={20} />
          </Box>
        ) : visibleNotifications.length === 0 ? (
          <Box sx={{ p: 2.5 }}>
            <Typography variant="body2" color="text.secondary" align="center">
              Нет уведомлений для выбранного фильтра
            </Typography>
          </Box>
        ) : (
          visibleNotifications.slice(0, 8).map((notification) => {
            const notificationText = getNotificationText(notification);
            const severity = getNotificationSeverity(notification);
            return (
              <MenuItem
                key={notification.id}
                onClick={() => handleNotificationClick(notification)}
                sx={(theme) => ({
                  alignItems: 'flex-start',
                  whiteSpace: 'normal',
                  borderRadius: 2,
                  mx: 0.5,
                  my: 0.25,
                  py: 1,
                  px: 1,
                  ...(notification.isRead
                    ? null
                    : {
                        backgroundColor: alpha(theme.palette.primary.main, 0.05),
                        border: `1px solid ${alpha(theme.palette.primary.main, 0.10)}`,
                      }),
                })}
              >
                <Stack direction="row" spacing={1.25} sx={{ width: '100%' }}>
                  <Box
                    sx={(theme) => ({
                      mt: 0.7,
                      width: 8,
                      height: 8,
                      borderRadius: '50%',
                      flexShrink: 0,
                      bgcolor: notification.isRead ? alpha(theme.palette.text.secondary, 0.25) : theme.palette.primary.main,
                    })}
                  />
                  <Box sx={{ minWidth: 0, flexGrow: 1 }}>
                    <Stack direction="row" justifyContent="space-between" spacing={1}>
                      <Typography variant="body2" sx={{ fontWeight: 700 }}>
                        {notificationText.title}
                      </Typography>
                      <Stack direction="row" spacing={0.5} alignItems="center">
                        {severity !== 'low' && (
                          <MuiChip
                            size="small"
                            label={severity === 'high' ? 'Высокий' : 'Средний'}
                            color={severity === 'high' ? 'error' : 'warning'}
                            sx={{ height: 20 }}
                          />
                        )}
                        <Typography variant="caption" color="text.secondary" sx={{ whiteSpace: 'nowrap' }}>
                          {formatDateTime(notification.createdAt || notification.sentAt || notification.timestamp)}
                        </Typography>
                      </Stack>
                    </Stack>
                    <Typography
                      variant="caption"
                      color="text.secondary"
                      sx={{
                        display: 'block',
                        mt: 0.25,
                        lineHeight: 1.35,
                        overflow: 'hidden',
                        textOverflow: 'ellipsis',
                      }}
                    >
                      {notificationText.body}
                    </Typography>
                    <Stack direction="row" spacing={0.5} sx={{ mt: 0.75 }}>
                      {!notification.isRead && (
                        <Button
                          size="small"
                          variant="text"
                          onClick={(event) => {
                            event.stopPropagation();
                            markAsRead(notification.id);
                          }}
                        >
                          Подтв.
                        </Button>
                      )}
                      <Tooltip title="Отложить на 15 минут">
                        <span>
                          <IconButton size="small" onClick={(event) => handleSnoozeClick(event, notification.id)}>
                            <AccessTime fontSize="inherit" />
                          </IconButton>
                        </span>
                      </Tooltip>
                      <Tooltip title="Удалить">
                        <span>
                          <IconButton size="small" onClick={(event) => handleDeleteNotification(event, notification.id)}>
                            <DeleteOutline fontSize="inherit" />
                          </IconButton>
                        </span>
                      </Tooltip>
                    </Stack>
                  </Box>
                </Stack>
              </MenuItem>
            );
          })
        )}
      </Menu>

      <Menu
        anchorEl={profileAnchorEl}
        open={isProfileMenuOpen}
        onClose={handleProfileMenuClose}
        PaperProps={{
          sx: (theme) => ({
            mt: 1.2,
            borderRadius: 3,
            minWidth: 220,
            border: `1px solid ${alpha(theme.palette.primary.main, 0.12)}`,
            backgroundColor: alpha(theme.palette.background.paper, theme.palette.mode === 'dark' ? 0.94 : 0.94),
            backdropFilter: 'blur(18px)',
          }),
        }}
        transformOrigin={{ horizontal: 'right', vertical: 'top' }}
        anchorOrigin={{ horizontal: 'right', vertical: 'bottom' }}
      >
        <Box sx={{ px: 1.5, py: 1.25 }}>
          <Typography variant="body2" sx={{ fontWeight: 700 }}>
            {user?.username || 'Пользователь'}
          </Typography>
          <Typography variant="caption" color="text.secondary">
            {user?.email || 'Авторизованная сессия'}
          </Typography>
        </Box>
        <Divider />
        <MenuItem
          onClick={() => {
            navigate('/settings');
            handleProfileMenuClose();
          }}
        >
          <ListItemIcon>
            <Settings fontSize="small" />
          </ListItemIcon>
          Настройки
        </MenuItem>
        <MenuItem onClick={handleLogout}>
          <ListItemIcon>
            <Logout fontSize="small" />
          </ListItemIcon>
          Выйти
        </MenuItem>
      </Menu>

      <AlertDetailsDialog
        open={alertDetailsOpen}
        onClose={() => setAlertDetailsOpen(false)}
        alertItem={selectedAlert}
        activityItem={selectedActivity}
      />
    </Box>
  );
};

export default Layout;
