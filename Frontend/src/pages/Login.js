import React, { useState, useEffect } from 'react';
import {
  Box,
  Card,
  CardContent,
  Typography,
  TextField,
  Button,
  Alert,
  CircularProgress,
  Container,
  Avatar,
  InputAdornment,
  IconButton
} from '@mui/material';
import {
  Visibility,
  VisibilityOff,
  LockOutlined,
  Security
} from '@mui/icons-material';
import { useAuth } from '../contexts/AuthContext';
import { useNavigate, useLocation } from 'react-router-dom';

const Login = () => {
  const { login, user, loading: authLoading } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  
  const [formData, setFormData] = useState({
    username: '',
    password: ''
  });
  const [showPassword, setShowPassword] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');

  // Redirect if already logged in
  useEffect(() => {
    if (user && !authLoading) {
      const from = location.state?.from?.pathname || '/dashboard';
      navigate(from, { replace: true });
    }
  }, [user, authLoading, navigate, location]);

  const handleChange = (e) => {
    setFormData({
      ...formData,
      [e.target.name]: e.target.value
    });
    if (error) setError('');
  };

  const handleSubmit = async (e) => {
    e.preventDefault();
    
    if (!formData.username || !formData.password) {
      setError('Please enter both username and password');
      return;
    }

    try {
      setLoading(true);
      setError('');
      
      await login(formData.username, formData.password);
      
      // Navigation will be handled by the useEffect above
    } catch (err) {
      setError(err.message || 'Login failed. Please check your credentials.');
    } finally {
      setLoading(false);
    }
  };

  const handleTogglePasswordVisibility = () => {
    setShowPassword(!showPassword);
  };

  if (authLoading) {
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

  return (
    <Box
      sx={{
        minHeight: '100vh',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        background: 'linear-gradient(135deg, #f5f7fa 0%, #c3cfe2 100%)',
        py: 4
      }}
    >
      <Container maxWidth="sm">
        <Card
          sx={{
            boxShadow: '0 8px 32px rgba(0, 0, 0, 0.1)',
            borderRadius: 2,
            overflow: 'hidden'
          }}
        >
          <CardContent sx={{ p: 4 }}>
            <Box display="flex" flexDirection="column" alignItems="center" mb={4}>
              <Avatar
                sx={{
                  mb: 2,
                  width: 64,
                  height: 64,
                  backgroundColor: 'primary.main',
                  boxShadow: '0 4px 12px rgba(25, 118, 210, 0.3)'
                }}
              >
                <LockOutlined fontSize="large" />
              </Avatar>
              <Typography variant="h4" component="h1" gutterBottom color="primary">
                Activity Monitor
              </Typography>
              <Typography variant="body2" color="text.secondary" align="center">
                Sign in to access the user activity monitoring system
              </Typography>
            </Box>

            {error && (
              <Alert severity="error" sx={{ mb: 3 }}>
                {error}
              </Alert>
            )}

            <Box
              component="form"
              onSubmit={handleSubmit}
              sx={{ width: '100%' }}
            >
              <TextField
                fullWidth
                label="Username"
                name="username"
                value={formData.username}
                onChange={handleChange}
                margin="normal"
                variant="outlined"
                autoComplete="username"
                autoFocus
                disabled={loading}
                sx={{ mb: 2 }}
              />

              <TextField
                fullWidth
                label="Password"
                name="password"
                type={showPassword ? 'text' : 'password'}
                value={formData.password}
                onChange={handleChange}
                margin="normal"
                variant="outlined"
                autoComplete="current-password"
                disabled={loading}
                sx={{ mb: 3 }}
                InputProps={{
                  endAdornment: (
                    <InputAdornment position="end">
                      <IconButton
                        aria-label="toggle password visibility"
                        onClick={handleTogglePasswordVisibility}
                        edge="end"
                        disabled={loading}
                      >
                        {showPassword ? <VisibilityOff /> : <Visibility />}
                      </IconButton>
                    </InputAdornment>
                  )
                }}
              />

              <Button
                type="submit"
                fullWidth
                variant="contained"
                size="large"
                disabled={loading}
                sx={{
                  py: 1.5,
                  mb: 2,
                  textTransform: 'none',
                  fontSize: '1.1rem',
                  fontWeight: 600,
                  boxShadow: '0 4px 12px rgba(25, 118, 210, 0.3)'
                }}
              >
                {loading ? (
                  <CircularProgress size={24} color="inherit" />
                ) : (
                  'Sign In'
                )}
              </Button>
            </Box>

            <Box display="flex" justifyContent="center" mt={3}>
              <Box display="flex" alignItems="center" color="text.secondary">
                <Security sx={{ mr: 1, fontSize: 20 }} />
                <Typography variant="caption">
                  Secure authentication with JWT tokens
                </Typography>
              </Box>
            </Box>
          </CardContent>
        </Card>

        <Box mt={3} textAlign="center">
          <Typography variant="body2" color="text.secondary">
            Demo Credentials: admin / admin123
          </Typography>
        </Box>
      </Container>
    </Box>
  );
};

export default Login;