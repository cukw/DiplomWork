import React from 'react';
import {
  Box,
  Chip,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Divider,
  Button,
  Grid,
  Typography,
} from '@mui/material';

const toDateTimeRu = (value) => {
  if (!value) return '—';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return String(value);
  return date.toLocaleString('ru-RU');
};

const AlertDetailsDialog = ({
  open,
  onClose,
  alertItem,
  activityItem,
  title = 'Детали тревоги',
}) => {
  const severity = alertItem?.severity || 'Низкий';
  const severityColor = String(severity).toLowerCase().includes('high') || String(severity).toLowerCase().includes('выс')
    ? 'error'
    : String(severity).toLowerCase().includes('med') || String(severity).toLowerCase().includes('сред')
      ? 'warning'
      : 'info';

  return (
    <Dialog open={open} onClose={onClose} maxWidth="md" fullWidth>
      <DialogTitle>{title}</DialogTitle>
      <DialogContent dividers>
        {!alertItem ? (
          <Typography variant="body2" color="text.secondary">
            Нет данных для отображения.
          </Typography>
        ) : (
          <Box>
            <Grid container spacing={2}>
              <Grid item xs={12} sm={6}>
                <Typography variant="caption" color="text.secondary">ID тревоги</Typography>
                <Typography variant="body2">{alertItem.id ?? '—'}</Typography>
              </Grid>
              <Grid item xs={12} sm={6}>
                <Typography variant="caption" color="text.secondary">Тип</Typography>
                <Typography variant="body2">{alertItem.type || alertItem.title || '—'}</Typography>
              </Grid>
              <Grid item xs={12} sm={6}>
                <Typography variant="caption" color="text.secondary">Серьезность</Typography>
                <Box mt={0.5}>
                  <Chip size="small" color={severityColor} label={severity} />
                </Box>
              </Grid>
              <Grid item xs={12} sm={6}>
                <Typography variant="caption" color="text.secondary">Обнаружено</Typography>
                <Typography variant="body2">{toDateTimeRu(alertItem.detectedAt || alertItem.timestamp || alertItem.sentAt)}</Typography>
              </Grid>
              <Grid item xs={12}>
                <Typography variant="caption" color="text.secondary">Описание</Typography>
                <Typography variant="body2">{alertItem.description || alertItem.message || '—'}</Typography>
              </Grid>
              <Grid item xs={12} sm={6}>
                <Typography variant="caption" color="text.secondary">Связанная активность (ID)</Typography>
                <Typography variant="body2">{alertItem.activityId ?? '—'}</Typography>
              </Grid>
              <Grid item xs={12} sm={6}>
                <Typography variant="caption" color="text.secondary">Источник</Typography>
                <Typography variant="body2">{alertItem.channel || alertItem.source || '—'}</Typography>
              </Grid>
              <Grid item xs={12} sm={6}>
                <Typography variant="caption" color="text.secondary">Статус прочтения</Typography>
                <Typography variant="body2">{alertItem.isRead || alertItem.is_read ? 'Прочитано' : 'Не прочитано'}</Typography>
              </Grid>
              <Grid item xs={12} sm={6}>
                <Typography variant="caption" color="text.secondary">Подтверждение</Typography>
                <Typography variant="body2">{alertItem.acknowledged ? 'Подтверждено' : 'Не подтверждено'}</Typography>
              </Grid>
            </Grid>

            <Divider sx={{ my: 2 }} />

            <Typography variant="subtitle2" sx={{ mb: 1 }}>Связанная активность</Typography>
            {!activityItem ? (
              <Typography variant="body2" color="text.secondary">
                Связанная активность не найдена в текущей выборке.
              </Typography>
            ) : (
              <Grid container spacing={2}>
                <Grid item xs={12} sm={6}>
                  <Typography variant="caption" color="text.secondary">ID активности</Typography>
                  <Typography variant="body2">{activityItem.id ?? '—'}</Typography>
                </Grid>
                <Grid item xs={12} sm={6}>
                  <Typography variant="caption" color="text.secondary">Время</Typography>
                  <Typography variant="body2">{toDateTimeRu(activityItem.timestamp)}</Typography>
                </Grid>
                <Grid item xs={12} sm={6}>
                  <Typography variant="caption" color="text.secondary">Тип активности</Typography>
                  <Typography variant="body2">{activityItem.activityType || '—'}</Typography>
                </Grid>
                <Grid item xs={12} sm={6}>
                  <Typography variant="caption" color="text.secondary">Компьютер</Typography>
                  <Typography variant="body2">{activityItem.computerId ?? '—'}</Typography>
                </Grid>
                <Grid item xs={12} sm={6}>
                  <Typography variant="caption" color="text.secondary">Процесс</Typography>
                  <Typography variant="body2">{activityItem.processName || '—'}</Typography>
                </Grid>
                <Grid item xs={12} sm={6}>
                  <Typography variant="caption" color="text.secondary">Риск</Typography>
                  <Typography variant="body2">{Number(activityItem.riskScore || 0).toFixed(1)}</Typography>
                </Grid>
                <Grid item xs={12}>
                  <Typography variant="caption" color="text.secondary">URL / Контекст</Typography>
                  <Typography variant="body2">{activityItem.url || activityItem.details || '—'}</Typography>
                </Grid>
              </Grid>
            )}
          </Box>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Закрыть</Button>
      </DialogActions>
    </Dialog>
  );
};

export default AlertDetailsDialog;
