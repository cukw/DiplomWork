import React from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { useAuth } from '../contexts/AuthContext';
import { Alert, Box, Button, CircularProgress, Stack } from '@mui/material';

const ProtectedRoute = ({ children }) => {
  const { user, loading, logout } = useAuth();
  const location = useLocation();

  if (loading) {
    return (
      <Box
        display="flex"
        justifyContent="center"
        alignItems="center"
        minHeight="100vh"
        sx={{ backgroundColor: 'background.default' }}
      >
        <CircularProgress />
      </Box>
    );
  }

  if (!user) {
    // Redirect them to the /login page, but save the current location they were
    // trying to go to. This allows us to send them along to that page after they
    // login, which is a nicer user experience than dropping them off on the home page.
    return <Navigate to="/login" state={{ from: location }} replace />;
  }

  if (String(user.role || '').toLowerCase() !== 'admin') {
    return (
      <Box
        display="flex"
        justifyContent="center"
        alignItems="center"
        minHeight="100vh"
        sx={{ backgroundColor: 'background.default', p: 2 }}
      >
        <Stack spacing={2} sx={{ width: '100%', maxWidth: 520 }}>
          <Alert severity="error">
            Панель доступна только администраторам.
          </Alert>
          <Button variant="contained" onClick={() => logout({ skipServerLogout: true })}>
            Выйти
          </Button>
        </Stack>
      </Box>
    );
  }

  return children;
};

export default ProtectedRoute;
