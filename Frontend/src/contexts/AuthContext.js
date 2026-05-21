import React, { createContext, useContext, useState, useEffect } from 'react';
import { authAPI } from '../services/api';

// Create context for authentication
const AuthContext = createContext();

export const AuthProvider = ({ children }) => {
  const [authState, setAuthState] = useState({
    isAuthenticated: false,
    user: null,
    token: null,
    loading: false,
    error: null
  });

  // Check for existing token on mount
  useEffect(() => {
    const token = localStorage.getItem('token');
    if (token) {
      const storedUser = JSON.parse(localStorage.getItem('user') || '{}');
      if (String(storedUser?.role || '').toLowerCase() !== 'admin') {
        localStorage.removeItem('token');
        localStorage.removeItem('user');
        return;
      }

      setAuthState({
        isAuthenticated: true,
        user: storedUser,
        token: token,
        loading: false,
        error: null
      });
    }
  }, []);

  const login = async (username, password) => {
    try {
      setAuthState(prev => ({ ...prev, loading: true, error: null }));
      
      const response = await authAPI.login(username, password);
      
      if (response.token) {
        const { token, user } = response;
        if (String(user?.role || '').toLowerCase() !== 'admin') {
          const errorMessage = 'Панель доступна только администраторам';
          setAuthState({
            isAuthenticated: false,
            user: null,
            token: null,
            loading: false,
            error: errorMessage
          });
          throw new Error(errorMessage);
        }

        localStorage.setItem('token', token);
        localStorage.setItem('user', JSON.stringify(user));
        
        setAuthState({
          isAuthenticated: true,
          user,
          token,
          loading: false,
          error: null
        });
        
        return { success: true };
      } else {
        setAuthState({
          isAuthenticated: false,
          user: null,
          token: null,
          loading: false,
          error: response.message || 'Login failed'
        });
        
        return { success: false, error: response.message };
      }
    } catch (error) {
      const errorMessage = error.response?.data?.message || error.message || 'An error occurred during login';
      setAuthState({
        isAuthenticated: false,
        user: null,
        token: null,
        loading: false,
        error: errorMessage
      });
      
      throw new Error(errorMessage);
    }
  };

  const logout = async (options = {}) => {
    const skipServerLogout = options?.skipServerLogout === true;

    try {
      if (!skipServerLogout) {
        // Вызываем API для логаута на сервере
        await authAPI.logout();
      }
    } catch (error) {
      console.error('Logout API error:', error);
      // Продолжаем с локальным логаутом даже если API не сработал
    }
    
    // Удаляем локальные данные
    localStorage.removeItem('token');
    localStorage.removeItem('user');
    
    setAuthState({
      isAuthenticated: false,
      user: null,
      token: null,
      loading: false,
      error: null
    });
  };

  const value = {
    ...authState,
    login,
    logout,
    user: authState.user,
    loading: authState.loading,
    isAuthenticated: authState.isAuthenticated
  };

  return (
    <AuthContext.Provider value={value}>
      {children}
    </AuthContext.Provider>
  );
};

export const useAuth = () => {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
};
