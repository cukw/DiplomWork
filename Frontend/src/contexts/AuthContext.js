import React, { createContext, useContext, useState, useEffect } from 'react';
import axios from 'axios';

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
      setAuthState({
        isAuthenticated: true,
        user: JSON.parse(localStorage.getItem('user') || '{}'),
        token: token,
        loading: false,
        error: null
      });
    }
  }, []);

  const login = async (username, password) => {
    try {
      setAuthState(prev => ({ ...prev, loading: true, error: null }));
      
      const response = await axios.post('/api/auth/login', { username, password });
      
      if (response.data.success) {
        const { token, user } = response.data;
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
          error: response.data.message || 'Login failed'
        });
        
        return { success: false, error: response.data.message };
      }
    } catch (error) {
      setAuthState({
        isAuthenticated: false,
        user: null,
        token: null,
        loading: false,
        error: error.message || 'An error occurred during login'
      });
      
      return { success: false, error: error.message };
    }
  };

  const logout = () => {
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

  const value = { authState, login, logout };

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