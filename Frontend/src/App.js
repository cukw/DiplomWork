import React, { useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { BrowserRouter as Router, Routes, Route, Navigate } from 'react-router-dom';
import { ThemeProvider, createTheme, responsiveFontSizes, alpha } from '@mui/material/styles';
import CssBaseline from '@mui/material/CssBaseline';
import GlobalStyles from '@mui/material/GlobalStyles';
import { AuthProvider, useAuth } from './contexts/AuthContext';
import { NotificationProvider } from './contexts/NotificationContext';
import { setLogoutHandler, setNavigateHandler } from './services/api';
import Layout from './components/Layout';
import Login from './pages/Login';
import Dashboard from './pages/Dashboard';
import Users from './pages/Users';
import Agents from './pages/Agents';
import Reports from './pages/Reports';
import Settings from './pages/Settings';
import Analytics from './pages/Analytics';
import ProtectedRoute from './components/ProtectedRoute';
import ErrorBoundary from './components/ErrorBoundary';
import { ThemeModeContext } from './contexts/ThemeModeContext';

const THEME_MODE_STORAGE_KEY = 'ams-theme-mode';

const getInitialThemeMode = () => {
  try {
    const saved = localStorage.getItem(THEME_MODE_STORAGE_KEY);
    if (saved === 'light' || saved === 'dark') return saved;
  } catch {
    // ignore localStorage issues
  }

  return window.matchMedia?.('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
};

const buildAppTheme = (mode) => {
  const isDark = mode === 'dark';

  let theme = createTheme({
    palette: {
      mode,
      primary: isDark
        ? { main: '#facc15', light: '#fde047', dark: '#eab308' }
        : { main: '#2563eb', light: '#60a5fa', dark: '#1d4ed8' },
      secondary: isDark
        ? { main: '#f59e0b', light: '#fbbf24', dark: '#d97706' }
        : { main: '#0ea5e9', light: '#38bdf8', dark: '#0284c7' },
      success: { main: isDark ? '#84cc16' : '#16a34a' },
      warning: { main: isDark ? '#f59e0b' : '#d97706' },
      error: { main: isDark ? '#f87171' : '#dc2626' },
      info: { main: isDark ? '#38bdf8' : '#0284c7' },
      background: isDark
        ? {
            default: '#080808',
            paper: '#131313',
          }
        : {
            default: '#f4f9ff',
            paper: '#ffffff',
          },
      text: isDark
        ? {
            primary: '#f8fafc',
            secondary: '#cbd5e1',
          }
        : {
            primary: '#0f172a',
            secondary: '#475569',
          },
      divider: isDark ? alpha('#facc15', 0.14) : alpha('#2563eb', 0.12),
    },
    shape: {
      borderRadius: 14,
    },
    typography: {
      fontFamily: '"IBM Plex Sans", "Segoe UI", sans-serif',
      h1: { fontFamily: '"Space Grotesk", "IBM Plex Sans", sans-serif', fontWeight: 700, letterSpacing: '-0.02em' },
      h2: { fontFamily: '"Space Grotesk", "IBM Plex Sans", sans-serif', fontWeight: 700, letterSpacing: '-0.02em' },
      h3: { fontFamily: '"Space Grotesk", "IBM Plex Sans", sans-serif', fontWeight: 700, letterSpacing: '-0.02em' },
      h4: { fontFamily: '"Space Grotesk", "IBM Plex Sans", sans-serif', fontWeight: 700, letterSpacing: '-0.02em' },
      h5: { fontFamily: '"Space Grotesk", "IBM Plex Sans", sans-serif', fontWeight: 700, letterSpacing: '-0.02em' },
      h6: { fontFamily: '"Space Grotesk", "IBM Plex Sans", sans-serif', fontWeight: 700, letterSpacing: '-0.01em' },
      button: { fontWeight: 600 },
    },
    components: {
      MuiCssBaseline: {
        styleOverrides: {
          ':root': {
            colorScheme: mode,
          },
        },
      },
      MuiPaper: {
        styleOverrides: {
          root: ({ theme }) => ({
            borderRadius: theme.shape.borderRadius,
            border: `1px solid ${alpha(theme.palette.primary.main, theme.palette.mode === 'dark' ? 0.14 : 0.08)}`,
            backgroundImage: 'none',
          }),
        },
      },
      MuiCard: {
        styleOverrides: {
          root: ({ theme }) => ({
            position: 'relative',
            borderRadius: theme.shape.borderRadius + 4,
            border: `1px solid ${alpha(theme.palette.primary.main, theme.palette.mode === 'dark' ? 0.16 : 0.08)}`,
            boxShadow: `0 18px 45px ${alpha(theme.palette.common.black, theme.palette.mode === 'dark' ? 0.34 : 0.06)}`,
            background: theme.palette.mode === 'dark'
              ? `linear-gradient(180deg, ${alpha('#171717', 0.94)} 0%, ${alpha('#111111', 0.92)} 100%)`
              : `linear-gradient(180deg, ${alpha(theme.palette.common.white, 0.96)} 0%, ${alpha('#f8fbff', 0.90)} 100%)`,
            backdropFilter: 'blur(12px)',
          }),
        },
      },
      MuiAppBar: {
        styleOverrides: {
          root: ({ theme }) => ({
            backgroundImage: 'none',
            borderBottom: `1px solid ${alpha(theme.palette.primary.main, theme.palette.mode === 'dark' ? 0.18 : 0.08)}`,
            backdropFilter: 'blur(18px)',
          }),
        },
      },
      MuiDrawer: {
        styleOverrides: {
          paper: ({ theme }) => ({
            borderRight: `1px solid ${alpha(theme.palette.primary.main, theme.palette.mode === 'dark' ? 0.18 : 0.08)}`,
            background: theme.palette.mode === 'dark'
              ? `linear-gradient(180deg, ${alpha('#141414', 0.96)} 0%, ${alpha('#0d0d0d', 0.93)} 100%)`
              : `linear-gradient(180deg, ${alpha(theme.palette.common.white, 0.96)} 0%, ${alpha('#f4f9ff', 0.90)} 100%)`,
            backdropFilter: 'blur(18px)',
          }),
        },
      },
      MuiButton: {
        styleOverrides: {
          root: ({ theme }) => ({
            textTransform: 'none',
            borderRadius: 12,
            paddingInline: theme.spacing(1.8),
            boxShadow: 'none',
          }),
          contained: ({ theme }) => ({
            boxShadow: `0 10px 20px ${alpha(theme.palette.primary.main, theme.palette.mode === 'dark' ? 0.2 : 0.16)}`,
            '&:hover': {
              boxShadow: `0 14px 28px ${alpha(theme.palette.primary.main, theme.palette.mode === 'dark' ? 0.28 : 0.2)}`,
            },
          }),
        },
      },
      MuiChip: {
        styleOverrides: {
          root: {
            borderRadius: 999,
            fontWeight: 600,
          },
        },
      },
      MuiOutlinedInput: {
        styleOverrides: {
          root: ({ theme }) => ({
            borderRadius: 12,
            backgroundColor: theme.palette.mode === 'dark'
              ? alpha(theme.palette.background.paper, 0.72)
              : alpha(theme.palette.common.white, 0.85),
            transition: 'box-shadow 160ms ease, border-color 160ms ease, background-color 160ms ease',
            '&:hover .MuiOutlinedInput-notchedOutline': {
              borderColor: alpha(theme.palette.primary.main, theme.palette.mode === 'dark' ? 0.34 : 0.28),
            },
            '&.Mui-focused': {
              boxShadow: `0 0 0 4px ${alpha(theme.palette.primary.main, theme.palette.mode === 'dark' ? 0.16 : 0.10)}`,
            },
          }),
        },
      },
      MuiTabs: {
        styleOverrides: {
          root: ({ theme }) => ({
            minHeight: 48,
            borderRadius: 14,
            backgroundColor: alpha(theme.palette.background.paper, theme.palette.mode === 'dark' ? 0.58 : 0.72),
            border: `1px solid ${alpha(theme.palette.primary.main, theme.palette.mode === 'dark' ? 0.16 : 0.08)}`,
            padding: 4,
          }),
          indicator: ({ theme }) => ({
            height: '100%',
            borderRadius: 10,
            zIndex: 0,
            backgroundColor: alpha(theme.palette.primary.main, theme.palette.mode === 'dark' ? 0.16 : 0.10),
          }),
        },
      },
      MuiTab: {
        styleOverrides: {
          root: {
            minHeight: 40,
            textTransform: 'none',
            fontWeight: 600,
            borderRadius: 10,
            zIndex: 1,
          },
        },
      },
      MuiTableHead: {
        styleOverrides: {
          root: ({ theme }) => ({
            '& .MuiTableCell-root': {
              backgroundColor: alpha(theme.palette.primary.main, theme.palette.mode === 'dark' ? 0.12 : 0.05),
              color: theme.palette.text.secondary,
              fontWeight: 700,
              borderBottomColor: alpha(theme.palette.primary.main, theme.palette.mode === 'dark' ? 0.20 : 0.12),
            },
          }),
        },
      },
      MuiTableCell: {
        styleOverrides: {
          root: ({ theme }) => ({
            borderBottomColor: alpha(theme.palette.primary.main, theme.palette.mode === 'dark' ? 0.14 : 0.08),
          }),
        },
      },
      MuiAlert: {
        styleOverrides: {
          root: {
            borderRadius: 12,
          },
        },
      },
    },
  });

  theme = responsiveFontSizes(theme);
  return theme;
};

function AppRoutes() {
  const { logout } = useAuth();
  const navigate = useNavigate();
  
  // Устанавливаем обработчики для axios interceptor
  useEffect(() => {
    setLogoutHandler(logout);
    setNavigateHandler(navigate);
  }, [logout, navigate]);

  return (
    <Routes>
      <Route path="/login" element={<Login />} />
      <Route path="/" element={
        <ProtectedRoute>
          <Layout />
        </ProtectedRoute>
      }>
        <Route index element={<Navigate to="/dashboard" replace />} />
        <Route path="dashboard" element={<Dashboard />} />
        <Route path="agents" element={<Agents />} />
        <Route path="analytics" element={<Analytics />} />
        <Route path="reports" element={<Reports />} />
        <Route path="settings" element={<Settings />} />
        <Route path="users" element={<Users />} />
      </Route>
    </Routes>
  );
}

function App() {
  const [themeMode, setThemeMode] = useState(getInitialThemeMode);
  const theme = useMemo(() => buildAppTheme(themeMode), [themeMode]);

  useEffect(() => {
    try {
      localStorage.setItem(THEME_MODE_STORAGE_KEY, themeMode);
    } catch {
      // ignore localStorage issues
    }

    document.documentElement.setAttribute('data-theme', themeMode);
  }, [themeMode]);

  const themeModeValue = useMemo(() => ({
    mode: themeMode,
    setMode: setThemeMode,
    toggleMode: () => setThemeMode((prev) => (prev === 'dark' ? 'light' : 'dark')),
  }), [themeMode]);

  return (
    <ThemeModeContext.Provider value={themeModeValue}>
      <ThemeProvider theme={theme}>
        <CssBaseline />
        <GlobalStyles
          styles={(currentTheme) => ({
            '::selection': {
              backgroundColor: alpha(currentTheme.palette.primary.main, 0.18),
            },
            '.mui-page-shell': {
              animation: 'pageFadeIn 220ms ease-out',
            },
          })}
        />
        <ErrorBoundary>
          <AuthProvider>
            <NotificationProvider>
              <Router>
                <AppRoutes />
              </Router>
            </NotificationProvider>
          </AuthProvider>
        </ErrorBoundary>
      </ThemeProvider>
    </ThemeModeContext.Provider>
  );
}

export default App;
