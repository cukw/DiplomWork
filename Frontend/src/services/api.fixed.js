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

// Перехватчик для добавления токена аутентификации
api.interceptors.request.use(
  (config) => {
    const token = localStorage.getItem('token');
    if (token) {
      config.headers = config.headers || {};
      config.headers.Authorization = `Bearer ${token}`;
    }
    return config;
  },
  (error) => Promise.reject(error)
);

// Перехватчик для обработки ошибок аутентификации
api.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response?.status === 401) {
      localStorage.removeItem('token');
      localStorage.removeItem('user');
      window.location.href = '/login';
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

  getActivityById: async (id) => {
    const response = await api.get(`/search/activities/${id}`);
    return response.data;
  },

  createActivity: async (activityData) => {
    const response = await api.post('/search/activities', activityData);
    return response.data;
  },

  updateActivity: async (id, activityData) => {
    const response = await api.put(`/search/activities/${id}`, activityData);
    return response.data;
  },

  deleteActivity: async (id) => {
    const response = await api.delete(`/search/activities/${id}`);
    return response.data;
  },
};

// API методы для пользователей
export const userAPI = {
  getUsers: async (filters = {}) => {
    const params = new URLSearchParams();

    Object.keys(filters).forEach((key) => {
      if (filters[key] !== undefined && filters[key] !== '') {
        params.append(key, filters[key]);
      }
    });

    const response = await api.get(`/user/users?${params.toString()}`);
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
  getAgents: async () => {
    const response = await api.get('/agent/agents');
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
};

// Экспортируем экземпляр api для прямого использования
export default api;
