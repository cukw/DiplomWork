import React, { createContext, useContext, useState, useEffect } from 'react';
import { notificationAPI } from '../services/api';

// Контекст уведомлений
const NotificationContext = createContext();

export const NotificationProvider = ({ children }) => {
  const [notificationState, setNotificationState] = useState({
    notifications: [],
    unreadCount: 0,
    loading: false,
    error: null
  });

  // Загружаем уведомления при старте, если пользователь авторизован
  useEffect(() => {
    const token = localStorage.getItem('token');
    if (token) {
      fetchNotifications();
    }
  }, []);

  const fetchNotifications = async () => {
    try {
      setNotificationState(prev => ({ ...prev, loading: true, error: null }));
      
      const [response, unreadResponse] = await Promise.all([
        notificationAPI.getNotifications(),
        notificationAPI.getUnreadCount().catch(() => ({ count: null }))
      ]);

      const normalizeNotification = (n) => ({
        ...n,
        isRead: n?.isRead ?? n?.is_read ?? false
      });
      
      if (response && response.notifications) {
        const notifications = (response.notifications || []).map(normalizeNotification);
        const unreadCount = typeof unreadResponse?.count === 'number'
          ? unreadResponse.count
          : notifications.filter(n => !n.isRead).length;

        setNotificationState({
          notifications,
          unreadCount,
          loading: false,
          error: null
        });
      } else {
        // Если ответ не соответствует ожидаемому формату
        const notifications = (Array.isArray(response) ? response : []).map(normalizeNotification);
        setNotificationState({
          notifications,
          unreadCount: notifications.filter(n => !n.isRead).length,
          loading: false,
          error: null
        });
      }
    } catch (error) {
      console.error('Ошибка загрузки уведомлений:', error);
      
      // Изолируем ошибку, не даем ей сломать всё приложение
      setNotificationState({
        notifications: [],
        unreadCount: 0,
        loading: false,
        error: error.message || 'Ошибка при загрузке уведомлений'
      });
      
      // Не выбрасываем ошибку дальше, чтобы не сломать рендеринг
      // Это предотвращает белый экран при ошибках API
    }
  };

  const markAsRead = async (notificationId) => {
    try {
      // Используем notificationAPI вместо прямого вызова axios
      await notificationAPI.markAsRead(notificationId);
      
      setNotificationState(prev => ({
        ...prev,
        notifications: prev.notifications.map(n =>
          n.id === notificationId ? { ...n, isRead: true, is_read: true } : n
        ),
        unreadCount: Math.max(0, prev.unreadCount - 1)
      }));
    } catch (error) {
      console.error('Ошибка отметки уведомления как прочитанного:', error);
      setNotificationState(prev => ({
        ...prev,
        error: error.message || 'Не удалось отметить уведомление как прочитанное'
      }));
      // Не выбрасываем ошибку дальше, чтобы не сломать рендеринг
    }
  };

  const markAllAsRead = async () => {
    try {
      // Используем notificationAPI вместо прямого вызова axios
      await notificationAPI.markAllAsRead();
      
      setNotificationState(prev => ({
        ...prev,
        notifications: prev.notifications.map(n => ({ ...n, isRead: true, is_read: true })),
        unreadCount: 0
      }));
    } catch (error) {
      console.error('Ошибка отметки всех уведомлений как прочитанных:', error);
      setNotificationState(prev => ({
        ...prev,
        error: error.message || 'Не удалось отметить все уведомления как прочитанные'
      }));
      // Не выбрасываем ошибку дальше, чтобы не сломать рендеринг
    }
  };

  const deleteNotification = async (notificationId) => {
    try {
      await notificationAPI.deleteNotification(notificationId);
      setNotificationState((prev) => {
        const removed = prev.notifications.find((n) => n.id === notificationId);
        const wasUnread = removed && !(removed.isRead ?? removed.is_read);
        return {
          ...prev,
          notifications: prev.notifications.filter((n) => n.id !== notificationId),
          unreadCount: wasUnread ? Math.max(0, prev.unreadCount - 1) : prev.unreadCount,
        };
      });
    } catch (error) {
      console.error('Ошибка удаления уведомления:', error);
      setNotificationState(prev => ({
        ...prev,
        error: error.message || 'Не удалось удалить уведомление'
      }));
    }
  };

  const addNotification = (notification) => {
    if (!notification) return;

    const normalized = {
      id: notification.id ?? `local-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`,
      type: notification.type || 'info',
      title: notification.title || notification.type || 'Уведомление',
      message: notification.message || notification.description || '',
      timestamp: notification.timestamp || new Date().toISOString(),
      sentAt: notification.sentAt || notification.timestamp || new Date().toISOString(),
      isRead: Boolean(notification.isRead ?? notification.is_read ?? false),
      is_read: Boolean(notification.isRead ?? notification.is_read ?? false),
      channel: notification.channel || 'ui',
      localOnly: true,
    };

    setNotificationState((prev) => ({
      ...prev,
      notifications: [normalized, ...prev.notifications].slice(0, 100),
      unreadCount: normalized.isRead ? prev.unreadCount : prev.unreadCount + 1,
    }));
  };

  const applyLiveSnapshot = (snapshot) => {
    const nextUnreadCount = snapshot?.notifications?.unreadCount;
    if (typeof nextUnreadCount !== 'number') return;

    setNotificationState((prev) => ({
      ...prev,
      unreadCount: nextUnreadCount,
    }));
  };

  const clearError = () => {
    setNotificationState(prev => ({ ...prev, error: null }));
  };

  const value = {
    ...notificationState,
    fetchNotifications,
    markAsRead,
    markAllAsRead,
    deleteNotification,
    addNotification,
    applyLiveSnapshot,
    clearError
  };

  return (
    <NotificationContext.Provider value={value}>
      {children}
    </NotificationContext.Provider>
  );
};

export const useNotifications = () => {
  const context = useContext(NotificationContext);
  if (!context) {
    throw new Error('useNotifications должен использоваться внутри NotificationProvider');
  }
  return context;
};
