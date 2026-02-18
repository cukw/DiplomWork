import React, { createContext, useContext, useState, useEffect } from 'react';
import axios from 'axios';

// Create context for notifications
const NotificationContext = createContext();

export const NotificationProvider = ({ children }) => {
  const [notificationState, setNotificationState] = useState({
    notifications: [],
    unreadCount: 0,
    loading: false,
    error: null
  });

  // Fetch notifications on mount and when authenticated
  useEffect(() => {
    const token = localStorage.getItem('token');
    if (token) {
      fetchNotifications();
    }
  }, []);

  const fetchNotifications = async () => {
    try {
      setNotificationState(prev => ({ ...prev, loading: true, error: null }));
      
      const token = localStorage.getItem('token');
      const response = await axios.get('/api/notifications', {
        headers: {
          Authorization: `Bearer ${token}`
        }
      });
      
      if (response.data.success) {
        setNotificationState({
          notifications: response.data.notifications || [],
          unreadCount: response.data.unreadCount || 0,
          loading: false,
          error: null
        });
      } else {
        setNotificationState({
          notifications: [],
          unreadCount: 0,
          loading: false,
          error: response.data.message || 'Failed to fetch notifications'
        });
      }
    } catch (error) {
      setNotificationState({
        notifications: [],
        unreadCount: 0,
        loading: false,
        error: error.message || 'An error occurred while fetching notifications'
      });
    }
  };

  const markAsRead = async (notificationId) => {
    try {
      const token = localStorage.getItem('token');
      
      const response = await axios.put(`/api/notifications/${notificationId}/read`, {}, {
        headers: {
          Authorization: `Bearer ${token}`
        }
      });
      
      if (response.data.success) {
        setNotificationState(prev => ({
          ...prev,
          notifications: prev.notifications.map(n => 
            n.id === notificationId ? { ...n, is_read: true } : n
          ),
          unreadCount: Math.max(0, prev.unreadCount - 1)
        }));
      }
    } catch (error) {
      setNotificationState(prev => ({
        ...prev,
        error: error.message || 'Failed to mark notification as read'
      }));
    }
  };

  const markAllAsRead = async () => {
    try {
      const token = localStorage.getItem('token');
      
      const response = await axios.put('/api/notifications/read-all', {}, {
        headers: {
          Authorization: `Bearer ${token}`
        }
      });
      
      if (response.data.success) {
        setNotificationState(prev => ({
          ...prev,
          notifications: prev.notifications.map(n => ({ ...n, is_read: true })),
          unreadCount: 0
        }));
      }
    } catch (error) {
      setNotificationState(prev => ({
        ...prev,
        error: error.message || 'Failed to mark all notifications as read'
      }));
    }
  };

  const value = { 
    notificationState, 
    fetchNotifications, 
    markAsRead, 
    markAllAsRead 
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
    throw new Error('useNotifications must be used within a NotificationProvider');
  }
  return context;
};