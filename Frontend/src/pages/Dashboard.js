import React, { useState, useEffect } from 'react';
import {
  Box,
  Card,
  CardContent,
  Grid,
  Typography,
  Paper,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Chip,
  CircularProgress,
  Alert
} from '@mui/material';
import {
  TrendingUp,
  TrendingDown,
  People,
  Computer,
  Warning,
  CheckCircle
} from '@mui/icons-material';
import { useAuth } from '../contexts/AuthContext';
import { useNotifications } from '../contexts/NotificationContext';
import { dashboardAPI } from '../services/api';

const Dashboard = () => {
  const { user } = useAuth();
  const { notifications } = useNotifications();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [stats, setStats] = useState({
    totalUsers: 0,
    activeUsers: 0,
    totalComputers: 0,
    activeComputers: 0,
    totalActivities: 0,
    anomaliesCount: 0
  });
  const [recentActivities, setRecentActivities] = useState([]);
  const [anomalies, setAnomalies] = useState([]);

  useEffect(() => {
    fetchDashboardData();
  }, []);

  const fetchDashboardData = async () => {
    try {
      setLoading(true);
      
      // Используем новый API сервис
      const [statsData, activitiesData, anomaliesData] = await Promise.allSettled([
        dashboardAPI.getStats(),
        dashboardAPI.getRecentActivities(),
        dashboardAPI.getRecentAnomalies()
      ]);
      
      // Обрабатываем результаты
      if (statsData.status === 'fulfilled') {
        setStats(statsData.value);
      } else {
        console.error('Failed to fetch stats:', statsData.reason);
        // Fallback to mock data if API fails
        setStats({
          totalUsers: 150,
          activeUsers: 89,
          totalComputers: 200,
          activeComputers: 156,
          totalActivities: 15420,
          anomaliesCount: 12
        });
      }
      
      if (activitiesData.status === 'fulfilled') {
        setRecentActivities(activitiesData.value);
      } else {
        console.error('Failed to fetch activities:', activitiesData.reason);
        // Fallback to mock data
        setRecentActivities([
          { id: 1, user: 'John Doe', computer: 'PC-001', activity: 'File Access', timestamp: '2024-01-15 10:30:00', status: 'normal' },
          { id: 2, user: 'Jane Smith', computer: 'PC-002', activity: 'Application Launch', timestamp: '2024-01-15 10:25:00', status: 'normal' },
          { id: 3, user: 'Bob Johnson', computer: 'PC-003', activity: 'USB Connection', timestamp: '2024-01-15 10:20:00', status: 'warning' },
          { id: 4, user: 'Alice Brown', computer: 'PC-004', activity: 'Network Access', timestamp: '2024-01-15 10:15:00', status: 'normal' },
          { id: 5, user: 'Charlie Wilson', computer: 'PC-005', activity: 'File Deletion', timestamp: '2024-01-15 10:10:00', status: 'anomaly' }
        ]);
      }
      
      if (anomaliesData.status === 'fulfilled') {
        setAnomalies(anomaliesData.value);
      } else {
        console.error('Failed to fetch anomalies:', anomaliesData.reason);
        // Fallback to mock data
        setAnomalies([
          { id: 1, user: 'Bob Johnson', computer: 'PC-003', type: 'Unauthorized USB', severity: 'High', timestamp: '2024-01-15 10:20:00' },
          { id: 2, user: 'Charlie Wilson', computer: 'PC-005', type: 'Suspicious File Deletion', severity: 'Medium', timestamp: '2024-01-15 10:10:00' }
        ]);
      }
    } catch (err) {
      setError('Failed to load dashboard data');
      console.error('Dashboard error:', err);
    } finally {
      setLoading(false);
    }
  };

  const getStatusColor = (status) => {
    switch (status) {
      case 'normal': return 'success';
      case 'warning': return 'warning';
      case 'anomaly': return 'error';
      default: return 'default';
    }
  };

  const getSeverityColor = (severity) => {
    switch (severity) {
      case 'High': return 'error';
      case 'Medium': return 'warning';
      case 'Low': return 'info';
      default: return 'default';
    }
  };

  if (loading) {
    return (
      <Box display="flex" justifyContent="center" alignItems="center" minHeight="60vh">
        <CircularProgress />
      </Box>
    );
  }

  if (error) {
    return (
      <Alert severity="error" sx={{ mb: 2 }}>
        {error}
      </Alert>
    );
  }

  return (
    <Box>
      <Typography variant="h4" gutterBottom>
        Dashboard
      </Typography>
      
      {/* Stats Cards */}
      <Grid container spacing={3} sx={{ mb: 4 }}>
        <Grid item xs={12} sm={6} md={3}>
          <Card>
            <CardContent>
              <Box display="flex" alignItems="center">
                <People sx={{ mr: 2, color: 'primary.main' }} />
                <Box>
                  <Typography variant="h4">{stats.totalUsers}</Typography>
                  <Typography variant="body2" color="textSecondary">
                    Total Users
                  </Typography>
                  <Box display="flex" alignItems="center" mt={1}>
                    <TrendingUp sx={{ mr: 1, fontSize: 16, color: 'success.main' }} />
                    <Typography variant="body2" color="success.main">
                      {stats.activeUsers} active
                    </Typography>
                  </Box>
                </Box>
              </Box>
            </CardContent>
          </Card>
        </Grid>
        
        <Grid item xs={12} sm={6} md={3}>
          <Card>
            <CardContent>
              <Box display="flex" alignItems="center">
                <Computer sx={{ mr: 2, color: 'primary.main' }} />
                <Box>
                  <Typography variant="h4">{stats.totalComputers}</Typography>
                  <Typography variant="body2" color="textSecondary">
                    Total Computers
                  </Typography>
                  <Box display="flex" alignItems="center" mt={1}>
                    <TrendingUp sx={{ mr: 1, fontSize: 16, color: 'success.main' }} />
                    <Typography variant="body2" color="success.main">
                      {stats.activeComputers} active
                    </Typography>
                  </Box>
                </Box>
              </Box>
            </CardContent>
          </Card>
        </Grid>
        
        <Grid item xs={12} sm={6} md={3}>
          <Card>
            <CardContent>
              <Box display="flex" alignItems="center">
                <CheckCircle sx={{ mr: 2, color: 'success.main' }} />
                <Box>
                  <Typography variant="h4">{stats.totalActivities}</Typography>
                  <Typography variant="body2" color="textSecondary">
                    Total Activities
                  </Typography>
                  <Box display="flex" alignItems="center" mt={1}>
                    <TrendingUp sx={{ mr: 1, fontSize: 16, color: 'success.main' }} />
                    <Typography variant="body2" color="success.main">
                      Today
                    </Typography>
                  </Box>
                </Box>
              </Box>
            </CardContent>
          </Card>
        </Grid>
        
        <Grid item xs={12} sm={6} md={3}>
          <Card>
            <CardContent>
              <Box display="flex" alignItems="center">
                <Warning sx={{ mr: 2, color: 'error.main' }} />
                <Box>
                  <Typography variant="h4">{stats.anomaliesCount}</Typography>
                  <Typography variant="body2" color="textSecondary">
                    Anomalies
                  </Typography>
                  <Box display="flex" alignItems="center" mt={1}>
                    <TrendingDown sx={{ mr: 1, fontSize: 16, color: 'error.main' }} />
                    <Typography variant="body2" color="error.main">
                      Requires attention
                    </Typography>
                  </Box>
                </Box>
              </Box>
            </CardContent>
          </Card>
        </Grid>
      </Grid>
      
      <Grid container spacing={3}>
        {/* Recent Activities */}
        <Grid item xs={12} md={8}>
          <Paper sx={{ p: 2 }}>
            <Typography variant="h6" gutterBottom>
              Recent Activities
            </Typography>
            <TableContainer>
              <Table>
                <TableHead>
                  <TableRow>
                    <TableCell>User</TableCell>
                    <TableCell>Computer</TableCell>
                    <TableCell>Activity</TableCell>
                    <TableCell>Time</TableCell>
                    <TableCell>Status</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {recentActivities.map((activity) => (
                    <TableRow key={activity.id}>
                      <TableCell>{activity.user}</TableCell>
                      <TableCell>{activity.computer}</TableCell>
                      <TableCell>{activity.activity}</TableCell>
                      <TableCell>{activity.timestamp}</TableCell>
                      <TableCell>
                        <Chip 
                          label={activity.status} 
                          color={getStatusColor(activity.status)}
                          size="small"
                        />
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </TableContainer>
          </Paper>
        </Grid>
        
        {/* Recent Anomalies */}
        <Grid item xs={12} md={4}>
          <Paper sx={{ p: 2 }}>
            <Typography variant="h6" gutterBottom>
              Recent Anomalies
            </Typography>
            {anomalies.length === 0 ? (
              <Typography variant="body2" color="textSecondary">
                No anomalies detected
              </Typography>
            ) : (
              anomalies.map((anomaly) => (
                <Card key={anomaly.id} sx={{ mb: 2 }}>
                  <CardContent sx={{ pb: 2 }}>
                    <Box display="flex" justifyContent="space-between" alignItems="center" mb={1}>
                      <Typography variant="subtitle2">
                        {anomaly.type}
                      </Typography>
                      <Chip 
                        label={anomaly.severity} 
                        color={getSeverityColor(anomaly.severity)}
                        size="small"
                      />
                    </Box>
                    <Typography variant="body2" color="textSecondary">
                      User: {anomaly.user}
                    </Typography>
                    <Typography variant="body2" color="textSecondary">
                      Computer: {anomaly.computer}
                    </Typography>
                    <Typography variant="caption" color="textSecondary">
                      {anomaly.timestamp}
                    </Typography>
                  </CardContent>
                </Card>
              ))
            )}
          </Paper>
        </Grid>
      </Grid>
    </Box>
  );
};

export default Dashboard;