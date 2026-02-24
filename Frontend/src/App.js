import React, { useEffect } from 'react';
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
import Reports from './pages/Reports';
import Settings from './pages/Settings';
import Analytics from './pages/Analytics';
import ProtectedRoute from './components/ProtectedRoute';
import ErrorBoundary from './components/ErrorBoundary';

let theme = createTheme({
  palette: {
    mode: 'light',
    primary: {
      main: '#0f766e',
      light: '#14b8a6',
      dark: '#115e59',
    },
    secondary: {
      main: '#b45309',
      light: '#d97706',
      dark: '#92400e',
    },
    success: {
      main: '#15803d',
    },
    warning: {
      main: '#c2410c',
    },
    error: {
      main: '#b91c1c',
    },
    info: {
      main: '#0369a1',
    },
    background: {
      default: '#edf2f7',
      paper: '#ffffff',
    },
    text: {
      primary: '#0f172a',
      secondary: '#475569',
    },
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
          colorScheme: 'light',
        },
      },
    },
    MuiPaper: {
      styleOverrides: {
        root: ({ theme }) => ({
          borderRadius: theme.shape.borderRadius,
          border: `1px solid ${alpha(theme.palette.common.white, 0.8)}`,
          backgroundImage: 'none',
        }),
      },
    },
    MuiCard: {
      styleOverrides: {
        root: ({ theme }) => ({
          position: 'relative',
          borderRadius: theme.shape.borderRadius + 4,
          border: `1px solid ${alpha(theme.palette.primary.main, 0.08)}`,
          boxShadow: `0 18px 45px ${alpha(theme.palette.common.black, 0.06)}`,
          background: `linear-gradient(180deg, ${alpha(theme.palette.common.white, 0.95)} 0%, ${alpha(theme.palette.common.white, 0.88)} 100%)`,
          backdropFilter: 'blur(12px)',
        }),
      },
    },
    MuiAppBar: {
      styleOverrides: {
        root: ({ theme }) => ({
          backgroundImage: 'none',
          borderBottom: `1px solid ${alpha(theme.palette.primary.main, 0.08)}`,
          backdropFilter: 'blur(18px)',
        }),
      },
    },
    MuiDrawer: {
      styleOverrides: {
        paper: ({ theme }) => ({
          borderRight: `1px solid ${alpha(theme.palette.primary.main, 0.08)}`,
          background: `linear-gradient(180deg, ${alpha(theme.palette.common.white, 0.95)} 0%, ${alpha(theme.palette.common.white, 0.88)} 100%)`,
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
          boxShadow: `0 10px 20px ${alpha(theme.palette.primary.main, 0.16)}`,
          '&:hover': {
            boxShadow: `0 14px 28px ${alpha(theme.palette.primary.main, 0.2)}`,
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
          backgroundColor: alpha(theme.palette.common.white, 0.85),
          transition: 'box-shadow 160ms ease, border-color 160ms ease',
          '&:hover .MuiOutlinedInput-notchedOutline': {
            borderColor: alpha(theme.palette.primary.main, 0.28),
          },
          '&.Mui-focused': {
            boxShadow: `0 0 0 4px ${alpha(theme.palette.primary.main, 0.10)}`,
          },
        }),
      },
    },
    MuiTabs: {
      styleOverrides: {
        root: ({ theme }) => ({
          minHeight: 48,
          borderRadius: 14,
          backgroundColor: alpha(theme.palette.common.white, 0.7),
          border: `1px solid ${alpha(theme.palette.primary.main, 0.08)}`,
          padding: 4,
        }),
        indicator: {
          height: '100%',
          borderRadius: 10,
          zIndex: 0,
          backgroundColor: alpha('#0f766e', 0.10),
        },
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
            backgroundColor: alpha(theme.palette.primary.main, 0.05),
            color: theme.palette.text.secondary,
            fontWeight: 700,
            borderBottomColor: alpha(theme.palette.primary.main, 0.12),
          },
        }),
      },
    },
    MuiTableCell: {
      styleOverrides: {
        root: ({ theme }) => ({
          borderBottomColor: alpha(theme.palette.primary.main, 0.08),
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
        <Route path="analytics" element={<Analytics />} />
        <Route path="reports" element={<Reports />} />
        <Route path="settings" element={<Settings />} />
        <Route path="users" element={<Users />} />
      </Route>
    </Routes>
  );
}

function App() {
  return (
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
  );
}

export default App;
