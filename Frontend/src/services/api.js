import axios from 'axios';

// Базовый URL для API
// Если переменная не задана — работаем через nginx location /api/ -> gateway
const API_BASE_URL = process.env.REACT_APP_API_URL || '/api';

// Создаем экземпляр axios с базовыми настройками
const api = axios.create({
  baseURL: API_BASE_URL,
  headers: {
    'Content-Type': 'application/json',
  },
});

export const normalizeStoredToken = (rawToken) => {
  if (!rawToken) return null;

  let token = String(rawToken).trim();
  if (!token || token === 'null' || token === 'undefined') return null;

  // Tolerate accidentally persisted formats like `"jwt"` or `Bearer jwt`
  if (token.startsWith('"') && token.endsWith('"') && token.length > 1) {
    token = token.slice(1, -1).trim();
  }
  if (token.toLowerCase().startsWith('bearer ')) {
    token = token.slice(7).trim();
  }

  return token || null;
};

// Перехватчик для добавления токена аутентификации
api.interceptors.request.use(
  (config) => {
    const token = normalizeStoredToken(localStorage.getItem('token'));
    config.headers = config.headers || {};

    if (token) {
      config.headers.Authorization = `Bearer ${token}`;
    } else if (config.headers.Authorization) {
      delete config.headers.Authorization;
    }

    return config;
  },
  (error) => Promise.reject(error)
);

// Глобальный обработчик для 401 ошибки
let logoutHandler = null;
let navigateHandler = null;
let unauthorizedHandlingInProgress = false;

export const setLogoutHandler = (handler) => {
  logoutHandler = handler;
};

export const setNavigateHandler = (handler) => {
  navigateHandler = handler;
};

// Улучшенный перехватчик для обработки ошибок
api.interceptors.response.use(
  (response) => response,
  async (error) => {
    if (error.response?.status === 401) {
      const requestUrl = String(error.config?.url || '');
      const isAuthRequest =
        requestUrl.includes('/auth/login') ||
        requestUrl.includes('/auth/register') ||
        requestUrl.includes('/auth/logout');

      if (isAuthRequest || unauthorizedHandlingInProgress) {
        return Promise.reject(error);
      }

      unauthorizedHandlingInProgress = true;

      // Удаляем токен
      localStorage.removeItem('token');
      localStorage.removeItem('user');
      
      try {
        // Вызываем обработчик выхода, если он установлен
        if (logoutHandler) {
          // При авто-logout после 401 не дергаем серверный logout повторно
          await logoutHandler({ skipServerLogout: true });
          
          // Используем SPA навигацию вместо полного перезагрузки страницы
          if (navigateHandler) {
            navigateHandler('/login');
          } else {
            console.warn('Navigate handler not set, using fallback');
            window.location.href = '/login';
          }
        } else {
          // Fallback только если обработчик не установлен
          console.warn('Logout handler not set, using fallback');
          window.location.href = '/login';
        }
      } finally {
        unauthorizedHandlingInProgress = false;
      }
    } else if (error.response?.status === 500) {
      console.error('Server error:', error.response.data);
      // Не показываем alert, а передаем ошибку дальше для обработки в компонентах
    }
    return Promise.reject(error);
  }
);

// API методы для аутентификации
export const authAPI = {
  login: async (username, password) => {
    const response = await api.post('/auth/login', { username, password });
    return response.data;
  },

  register: async (userData) => {
    const response = await api.post('/auth/register', userData);
    return response.data;
  },

  logout: async () => {
    const token = normalizeStoredToken(localStorage.getItem('token'));
    if (!token) {
      return { message: 'No active token' };
    }

    const response = await api.post('/auth/logout');
    return response.data;
  },

  getCurrentUser: async () => {
    const response = await api.get('/auth/me');
    return response.data;
  },
};

// API методы для дашборда
export const dashboardAPI = {
  getStats: async () => {
    const response = await api.get('/dashboard/stats');
    return response.data;
  },

  getRecentActivities: async () => {
    const response = await api.get('/dashboard/activities');
    return response.data;
  },

  getRecentAnomalies: async () => {
    const response = await api.get('/dashboard/anomalies');
    return response.data;
  },
};

// API методы для активности
export const activityAPI = {
  getActivities: async (filters = {}) => {
    const params = new URLSearchParams();

    Object.keys(filters).forEach((key) => {
      if (filters[key] !== undefined && filters[key] !== '') {
        params.append(key, filters[key]);
      }
    });

    const response = await api.get(`/search/activities?${params.toString()}`);
    return response.data;
  },

  getAnomalies: async (filters = {}) => {
    const params = new URLSearchParams();

    Object.keys(filters).forEach((key) => {
      if (filters[key] !== undefined && filters[key] !== '') {
        params.append(key, filters[key]);
      }
    });

    const response = await api.get(`/search/anomalies?${params.toString()}`);
    return response.data;
  },

  getSearchFilters: async () => {
    const response = await api.get('/search/filters');
    return response.data;
  },
};

// API методы для отчетов
export const reportsAPI = {
  getDailyReport: async (date) => {
    const response = await api.get('/reports/daily', { params: { date } });
    return response.data;
  },

  getWeeklyReport: async (startDate, endDate) => {
    const response = await api.get('/reports/weekly', { params: { startDate, endDate } });
    return response.data;
  },

  getMonthlyReport: async (month, year) => {
    const response = await api.get('/reports/monthly', { params: { month, year } });
    return response.data;
  },

  getCustomReport: async (startDate, endDate, filters = {}) => {
    const response = await api.get('/reports/custom', {
      params: { startDate, endDate, ...filters },
    });
    return response.data;
  },
};

// API методы для пользователей
export const userAPI = {
  getUsers: async (params = {}) => {
    const response = await api.get('/user/users', { params });
    return response.data;
  },

  getUserById: async (id) => {
    const response = await api.get(`/user/users/${id}`);
    return response.data;
  },

  createUser: async (userData) => {
    const response = await api.post('/user/users', userData);
    return response.data;
  },

  updateUser: async (id, userData) => {
    const response = await api.put(`/user/users/${id}`, userData);
    return response.data;
  },

  deleteUser: async (id) => {
    const response = await api.delete(`/user/users/${id}`);
    return response.data;
  },
};

// API методы для уведомлений (через текущий ASP.NET gateway controllers)
export const notificationAPI = {
  getNotifications: async () => {
    const response = await api.get('/notifications');
    return response.data;
  },

  markAsRead: async (id) => {
    const response = await api.put(`/notifications/${id}/read`);
    return response.data;
  },

  markAllAsRead: async () => {
    const response = await api.put('/notifications/read-all');
    return response.data;
  },

  getUnreadCount: async () => {
    const response = await api.get('/notifications/unread-count');
    return response.data;
  },

  deleteNotification: async (id) => {
    const response = await api.delete(`/notifications/${id}`);
    return response.data;
  },
};

// API методы для метрик
export const metricsAPI = {
  getMetrics: async (filters = {}) => {
    const params = new URLSearchParams();

    Object.keys(filters).forEach((key) => {
      if (filters[key] !== undefined && filters[key] !== '') {
        params.append(key, filters[key]);
      }
    });

    const response = await api.get(`/metrics/metrics?${params.toString()}`);
    return response.data;
  },
};

// API методы для управления агентами
export const agentAPI = {
  getAgents: async (params = {}) => {
    const response = await api.get('/agent/agents', { params });
    return response.data;
  },

  getAgentById: async (id) => {
    const response = await api.get(`/agent/agents/${id}`);
    return response.data;
  },

  createAgent: async (agentData) => {
    const response = await api.post('/agent/agents', agentData);
    return response.data;
  },

  updateAgent: async (id, agentData) => {
    const response = await api.put(`/agent/agents/${id}`, agentData);
    return response.data;
  },

  deleteAgent: async (id) => {
    const response = await api.delete(`/agent/agents/${id}`);
    return response.data;
  },

  syncAgent: async (id) => {
    const response = await api.post(`/agent/agents/${id}/sync`);
    return response.data;
  },

  getAgentPolicy: async (id) => {
    const response = await api.get(`/agent/agents/${id}/policy`);
    return response.data;
  },

  upsertAgentPolicy: async (id, payload) => {
    const response = await api.put(`/agent/agents/${id}/policy`, payload);
    return response.data;
  },

  deleteAgentPolicy: async (id) => {
    const response = await api.delete(`/agent/agents/${id}/policy`);
    return response.data;
  },

  getAgentPolicyVersions: async (id, params = {}) => {
    const response = await api.get(`/agent/agents/${id}/policy/versions`, { params });
    return response.data;
  },

  restoreAgentPolicyVersion: async (id, versionId, payload = {}) => {
    const response = await api.post(`/agent/agents/${id}/policy/versions/${versionId}/restore`, payload);
    return response.data;
  },

  getAgentCommands: async (id, params = {}) => {
    const response = await api.get(`/agent/agents/${id}/commands`, { params });
    return response.data;
  },

  createAgentCommand: async (id, payload) => {
    const response = await api.post(`/agent/agents/${id}/commands`, payload);
    return response.data;
  },

  blockWorkstation: async (id, reason = 'Blocked by admin') => {
    const response = await api.post(`/agent/agents/${id}/commands/block`, { reason });
    return response.data;
  },

  unblockWorkstation: async (id, reason = 'Unblocked by admin') => {
    const response = await api.post(`/agent/agents/${id}/commands/unblock`, { reason });
    return response.data;
  },
};

export const systemAPI = {
  getHealth: async () => {
    const response = await api.get('/system/health');
    return response.data;
  },
};

export const settingsAPI = {
  getSettings: async () => {
    const response = await api.get('/app-settings');
    return response.data;
  },

  saveSettings: async (payload) => {
    const response = await api.put('/app-settings', payload);
    return response.data;
  },

  getWhitelistEntries: async () => {
    const response = await api.get('/app-settings/whitelist');
    return response.data;
  },

  replaceWhitelistEntries: async (entries) => {
    const response = await api.put('/app-settings/whitelist', entries);
    return response.data;
  },

  createWhitelistEntry: async (entry) => {
    const response = await api.post('/app-settings/whitelist', entry);
    return response.data;
  },

  updateWhitelistEntry: async (id, entry) => {
    const response = await api.put(`/app-settings/whitelist/${id}`, entry);
    return response.data;
  },

  deleteWhitelistEntry: async (id) => {
    const response = await api.delete(`/app-settings/whitelist/${id}`);
    return response.data;
  },

  getBlacklistEntries: async () => {
    const response = await api.get('/app-settings/blacklist');
    return response.data;
  },

  replaceBlacklistEntries: async (entries) => {
    const response = await api.put('/app-settings/blacklist', entries);
    return response.data;
  },

  createBlacklistEntry: async (entry) => {
    const response = await api.post('/app-settings/blacklist', entry);
    return response.data;
  },

  updateBlacklistEntry: async (id, entry) => {
    const response = await api.put(`/app-settings/blacklist/${id}`, entry);
    return response.data;
  },

  deleteBlacklistEntry: async (id) => {
    const response = await api.delete(`/app-settings/blacklist/${id}`);
    return response.data;
  },
};

export const alertRulesAPI = {
  getRules: async () => {
    const response = await api.get('/alert-rules');
    return response.data;
  },

  getMetadata: async () => {
    const response = await api.get('/alert-rules/metadata');
    return response.data;
  },

  createRule: async (payload) => {
    const response = await api.post('/alert-rules', payload);
    return response.data;
  },

  updateRule: async (id, payload) => {
    const response = await api.put(`/alert-rules/${id}`, payload);
    return response.data;
  },

  setEnabled: async (id, enabled) => {
    const response = await api.patch(`/alert-rules/${id}/enabled`, { enabled });
    return response.data;
  },

  deleteRule: async (id) => {
    const response = await api.delete(`/alert-rules/${id}`);
    return response.data;
  },
};

export const liveAPI = {
  getStreamUrl: () => {
    const token = normalizeStoredToken(localStorage.getItem('token'));
    const separator = API_BASE_URL.includes('?') ? '&' : '?';
    const tokenPart = token ? `${separator}access_token=${encodeURIComponent(token)}` : '';
    return `${API_BASE_URL}/live/stream${tokenPart}`;
  },
};

// API методы для ReportService (generated reports / exports)
export const reportServiceAPI = {
  getDailyReportsRange: async (startDate, endDate, page = 1, pageSize = 20) => {
    const response = await api.get('/report/daily/range', {
      params: { startDate, endDate, page, pageSize },
    });
    return response.data;
  },

  getSummary: async (startDate, endDate, filters = {}) => {
    const response = await api.get('/report/summary', {
      params: { startDate, endDate, ...filters },
    });
    return response.data;
  },

  exportReport: async (payload) => {
    const response = await api.post('/report/export', payload);
    return response.data;
  },
};

// Экспортируем экземпляр api для прямого использования
export default api;
