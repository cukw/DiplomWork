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
  IconButton,
  Stack,
  Chip,
  Divider,
} from '@mui/material';
import {
  Visibility,
  VisibilityOff,
  LockOutlined,
  Security,
  ArrowForward,
} from '@mui/icons-material';
import { useAuth } from '../contexts/AuthContext';
import { useNavigate, useLocation } from 'react-router-dom';

const Login = () => {
  const { login, user, loading: authLoading } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();

  const [formData, setFormData] = useState({
    username: '',
    password: '',
  });
  const [showPassword, setShowPassword] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => {
    if (user && !authLoading) {
      const from = location.state?.from?.pathname || '/dashboard';
      navigate(from, { replace: true });
    }
  }, [user, authLoading, navigate, location]);

  const handleChange = (e) => {
    setFormData((prev) => ({
      ...prev,
      [e.target.name]: e.target.value,
    }));

    if (error) setError('');
  };

  const handleSubmit = async (e) => {
    e.preventDefault();

    if (!formData.username || !formData.password) {
      setError('Введите логин и пароль');
      return;
    }

    try {
      setLoading(true);
      setError('');
      await login(formData.username, formData.password);
    } catch (err) {
      setError(err.message || 'Не удалось выполнить вход. Проверьте учетные данные.');
    } finally {
      setLoading(false);
    }
  };

  const fillDemoCredentials = () => {
    setFormData({
      username: 'testuser',
      password: 'password',
    });
    setError('');
  };

  if (authLoading) {
    return (
      <Box
        display="flex"
        justifyContent="center"
        alignItems="center"
        minHeight="100vh"
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
        py: { xs: 3, md: 6 },
        position: 'relative',
        overflow: 'hidden',
      }}
    >
      <Box
        aria-hidden
        sx={(theme) => ({
          position: 'absolute',
          inset: 0,
          background:
            `radial-gradient(circle at 16% 18%, ${theme.palette.primary.main}14 0%, transparent 44%), ` +
            `radial-gradient(circle at 88% 20%, ${theme.palette.secondary.main}14 0%, transparent 42%), ` +
            `radial-gradient(circle at 50% 92%, ${theme.palette.info.main}12 0%, transparent 44%)`,
          pointerEvents: 'none',
        })}
      />

      <Container maxWidth="lg" sx={{ position: 'relative', zIndex: 1 }}>
        <Box
          sx={{
            display: 'grid',
            gridTemplateColumns: { xs: '1fr', md: '1.05fr 0.95fr' },
            gap: 3,
            alignItems: 'stretch',
          }}
        >
          <Card
            sx={(theme) => ({
              p: { xs: 1, md: 2 },
              minHeight: { md: 520 },
              display: 'flex',
              alignItems: 'stretch',
              background:
                `linear-gradient(160deg, ${theme.palette.common.white}f2 0%, ${theme.palette.common.white}d9 100%)`,
            })}
          >
            <CardContent sx={{ p: { xs: 2, md: 3 }, width: '100%', display: 'flex', flexDirection: 'column' }}>
              <Stack direction="row" spacing={1.5} alignItems="center" mb={3}>
                <Avatar
                  variant="rounded"
                  sx={(theme) => ({
                    width: 46,
                    height: 46,
                    borderRadius: 2.5,
                    bgcolor: `${theme.palette.primary.main}18`,
                    color: 'primary.dark',
                  })}
                >
                  <LockOutlined />
                </Avatar>
                <Box>
                  <Typography variant="h5">Вход в систему</Typography>
                  <Typography variant="body2" color="text.secondary">
                    Панель мониторинга активности сотрудников
                  </Typography>
                </Box>
              </Stack>

              <Stack direction="row" spacing={1} mb={3} flexWrap="wrap">
                <Chip size="small" label="JWT authentication" color="primary" variant="outlined" />
                <Chip size="small" label="Gateway routing" color="default" variant="outlined" />
                <Chip size="small" label="Audit ready" color="default" variant="outlined" />
              </Stack>

              {error && (
                <Alert severity="error" sx={{ mb: 2 }}>
                  {error}
                </Alert>
              )}

              <Box component="form" onSubmit={handleSubmit} sx={{ width: '100%' }}>
                <TextField
                  fullWidth
                  label="Логин"
                  name="username"
                  value={formData.username}
                  onChange={handleChange}
                  margin="normal"
                  autoComplete="username"
                  autoFocus
                  disabled={loading}
                />

                <TextField
                  fullWidth
                  label="Пароль"
                  name="password"
                  type={showPassword ? 'text' : 'password'}
                  value={formData.password}
                  onChange={handleChange}
                  margin="normal"
                  autoComplete="current-password"
                  disabled={loading}
                  sx={{ mt: 2 }}
                  InputProps={{
                    endAdornment: (
                      <InputAdornment position="end">
                        <IconButton
                          aria-label="toggle password visibility"
                          onClick={() => setShowPassword((prev) => !prev)}
                          edge="end"
                          disabled={loading}
                        >
                          {showPassword ? <VisibilityOff /> : <Visibility />}
                        </IconButton>
                      </InputAdornment>
                    ),
                  }}
                />

                <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5} mt={3}>
                  <Button
                    type="submit"
                    variant="contained"
                    size="large"
                    disabled={loading}
                    endIcon={loading ? null : <ArrowForward />}
                    sx={{ minWidth: 180, flex: { sm: 1 } }}
                  >
                    {loading ? <CircularProgress size={22} color="inherit" /> : 'Войти'}
                  </Button>
                  <Button
                    type="button"
                    variant="outlined"
                    size="large"
                    onClick={fillDemoCredentials}
                    disabled={loading}
                    sx={{ flex: { sm: 1 } }}
                  >
                    Заполнить демо
                  </Button>
                </Stack>
              </Box>

              <Divider sx={{ my: 3 }} />

              <Stack direction="row" spacing={1.25} alignItems="flex-start">
                <Security color="primary" sx={{ mt: 0.15, fontSize: 20 }} />
                <Box>
                  <Typography variant="body2" sx={{ fontWeight: 600 }}>
                    Тестовый доступ
                  </Typography>
                  <Typography variant="caption" color="text.secondary">
                    Используйте `testuser / password` для входа в локальном окружении.
                  </Typography>
                </Box>
              </Stack>
            </CardContent>
          </Card>

          <Card
            sx={(theme) => ({
              p: { xs: 1, md: 2 },
              display: 'flex',
              alignItems: 'stretch',
              minHeight: { md: 520 },
              background:
                `linear-gradient(180deg, ${theme.palette.primary.main}0d 0%, ${theme.palette.common.white}ee 28%, ${theme.palette.common.white}db 100%)`,
            })}
          >
            <CardContent sx={{ p: { xs: 2, md: 3 }, width: '100%' }}>
              <Typography variant="overline" color="primary" sx={{ letterSpacing: '0.12em', fontWeight: 700 }}>
                Security Workspace
              </Typography>
              <Typography variant="h4" sx={{ mt: 0.5, mb: 1.5 }}>
                Центр наблюдения за активностью
              </Typography>
              <Typography variant="body1" color="text.secondary" sx={{ mb: 3, lineHeight: 1.65 }}>
                Интерфейс предназначен для оперативного контроля событий, аномалий и отчётности по пользовательской
                активности. После входа откроется рабочая панель с live-метриками и журналом действий.
              </Typography>

              <Stack spacing={1.5}>
                {[
                  ['Gateway-first API', 'Все запросы проходят через единый gateway и JWT-проверку.'],
                  ['Anomaly feed', 'Выделение подозрительных действий и быстрый просмотр свежих инцидентов.'],
                  ['Reports & analytics', 'Экспорт и визуализация статистики по активности и рискам.'],
                ].map(([title, description]) => (
                  <Box
                    key={title}
                    sx={(theme) => ({
                      p: 1.75,
                      borderRadius: 2.5,
                      border: `1px solid ${theme.palette.primary.main}14`,
                      backgroundColor: `${theme.palette.common.white}aa`,
                    })}
                  >
                    <Typography variant="subtitle2" sx={{ fontWeight: 700, mb: 0.35 }}>
                      {title}
                    </Typography>
                    <Typography variant="body2" color="text.secondary">
                      {description}
                    </Typography>
                  </Box>
                ))}
              </Stack>
            </CardContent>
          </Card>
        </Box>
      </Container>
    </Box>
  );
};

export default Login;
