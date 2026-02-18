import React, { useState, useEffect } from 'react';
import {
  Box,
  Card,
  CardContent,
  Grid,
  Typography,
  Paper,
  CircularProgress,
  Alert,
  FormControl,
  InputLabel,
  Select,
  MenuItem,
  Button
} from '@mui/material';
import {
  BarChart,
  Bar,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  Legend,
  LineChart,
  Line,
  PieChart,
  Pie,
  Cell,
  ResponsiveContainer
} from 'recharts';
import { useAuth } from '../contexts/AuthContext';
import axios from 'axios';

const COLORS = ['#0088FE', '#00C49F', '#FFBB28', '#FF8042', '#8884D8'];

const Analytics = () => {
  const { user } = useAuth();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [reportType, setReportType] = useState('daily');
  const [reportData, setReportData] = useState(null);
  const [date, setDate] = useState(new Date().toISOString().split('T')[0]);
  const [startDate, setStartDate] = useState(new Date(Date.now() - 7 * 24 * 60 * 60 * 1000).toISOString().split('T')[0]);
  const [endDate, setEndDate] = useState(new Date().toISOString().split('T')[0]);

  useEffect(() => {
    fetchReportData();
  }, [reportType, date, startDate, endDate]);

  const fetchReportData = async () => {
    try {
      setLoading(true);
      const token = localStorage.getItem('token');
      const headers = {
        'Authorization': `Bearer ${token}`,
        'Content-Type': 'application/json'
      };

      let url;
      let params = {};

      switch (reportType) {
        case 'daily':
          url = '/api/reports/daily';
          params.date = date;
          break;
        case 'weekly':
          url = '/api/reports/weekly';
          params.startDate = startDate;
          break;
        case 'monthly':
          url = '/api/reports/monthly';
          const currentDate = new Date();
          params.year = currentDate.getFullYear();
          params.month = currentDate.getMonth() + 1;
          break;
        case 'custom':
          url = '/api/reports/custom';
          params.startDate = startDate;
          params.endDate = endDate;
          params.groupBy = 'day';
          break;
        default:
          url = '/api/reports/daily';
          params.date = date;
      }

      const response = await axios.get(url, { headers, params });
      setReportData(response.data);
    } catch (err) {
      setError('Failed to load report data');
      console.error('Analytics error:', err);
    } finally {
      setLoading(false);
    }
  };

  const handleRefresh = () => {
    fetchReportData();
  };

  if (loading) {
    return (
      <Box display="flex" justifyContent="center" alignItems="center" height="80vh">
        <CircularProgress />
      </Box>
    );
  }

  if (error) {
    return (
      <Box m={3}>
        <Alert severity="error">{error}</Alert>
      </Box>
    );
  }

  if (!reportData) {
    return (
      <Box m={3}>
        <Alert severity="info">No data available</Alert>
      </Box>
    );
  }

  return (
    <Box m={3}>
      <Typography variant="h4" gutterBottom>
        Activity Analytics
      </Typography>

      <Grid container spacing={3} mb={3}>
        <Grid item xs={12} md={3}>
          <FormControl fullWidth>
            <InputLabel>Report Type</InputLabel>
            <Select
              value={reportType}
              onChange={(e) => setReportType(e.target.value)}
            >
              <MenuItem value="daily">Daily</MenuItem>
              <MenuItem value="weekly">Weekly</MenuItem>
              <MenuItem value="monthly">Monthly</MenuItem>
              <MenuItem value="custom">Custom Range</MenuItem>
            </Select>
          </FormControl>
        </Grid>

        {reportType === 'daily' && (
          <Grid item xs={12} md={3}>
            <FormControl fullWidth>
              <InputLabel>Date</InputLabel>
              <Select
                value={date}
                onChange={(e) => setDate(e.target.value)}
              >
                {/* Generate last 30 days */}
                {Array.from({ length: 30 }, (_, i) => {
                  const d = new Date();
                  d.setDate(d.getDate() - i);
                  return (
                    <MenuItem key={i} value={d.toISOString().split('T')[0]}>
                      {d.toLocaleDateString()}
                    </MenuItem>
                  );
                })}
              </Select>
            </FormControl>
          </Grid>
        )}

        {reportType === 'weekly' && (
          <Grid item xs={12} md={3}>
            <FormControl fullWidth>
              <InputLabel>Start Date</InputLabel>
              <Select
                value={startDate}
                onChange={(e) => setStartDate(e.target.value)}
              >
                {/* Generate last 4 weeks */}
                {Array.from({ length: 4 }, (_, i) => {
                  const d = new Date();
                  d.setDate(d.getDate() - (i * 7));
                  return (
                    <MenuItem key={i} value={d.toISOString().split('T')[0]}>
                      {d.toLocaleDateString()}
                    </MenuItem>
                  );
                })}
              </Select>
            </FormControl>
          </Grid>
        )}

        {reportType === 'custom' && (
          <>
            <Grid item xs={12} md={3}>
              <FormControl fullWidth>
                <InputLabel>Start Date</InputLabel>
                <Select
                  value={startDate}
                  onChange={(e) => setStartDate(e.target.value)}
                >
                  {Array.from({ length: 30 }, (_, i) => {
                    const d = new Date();
                    d.setDate(d.getDate() - i);
                    return (
                      <MenuItem key={i} value={d.toISOString().split('T')[0]}>
                        {d.toLocaleDateString()}
                      </MenuItem>
                    );
                  })}
                </Select>
              </FormControl>
            </Grid>
            <Grid item xs={12} md={3}>
              <FormControl fullWidth>
                <InputLabel>End Date</InputLabel>
                <Select
                  value={endDate}
                  onChange={(e) => setEndDate(e.target.value)}
                >
                  {Array.from({ length: 30 }, (_, i) => {
                    const d = new Date();
                    d.setDate(d.getDate() - i);
                    return (
                      <MenuItem key={i} value={d.toISOString().split('T')[0]}>
                        {d.toLocaleDateString()}
                      </MenuItem>
                    );
                  })}
                </Select>
              </FormControl>
            </Grid>
          </>
        )}

        <Grid item xs={12} md={3}>
          <Button
            variant="contained"
            color="primary"
            onClick={handleRefresh}
            fullWidth
            style={{ height: '56px' }}
          >
            Refresh
          </Button>
        </Grid>
      </Grid>

      {/* Summary Cards */}
      <Grid container spacing={3} mb={3}>
        <Grid item xs={12} md={3}>
          <Card>
            <CardContent>
              <Typography variant="h6" color="textSecondary">
                Total Activities
              </Typography>
              <Typography variant="h4">
                {reportData.summary?.totalActivities || 0}
              </Typography>
            </CardContent>
          </Card>
        </Grid>
        <Grid item xs={12} md={3}>
          <Card>
            <CardContent>
              <Typography variant="h6" color="textSecondary">
                Total Anomalies
              </Typography>
              <Typography variant="h4">
                {reportData.summary?.totalAnomalies || 0}
              </Typography>
            </CardContent>
          </Card>
        </Grid>
        <Grid item xs={12} md={3}>
          <Card>
            <CardContent>
              <Typography variant="h6" color="textSecondary">
                Blocked Activities
              </Typography>
              <Typography variant="h4">
                {reportData.summary?.blockedActivities || 0}
              </Typography>
            </CardContent>
          </Card>
        </Grid>
        <Grid item xs={12} md={3}>
          <Card>
            <CardContent>
              <Typography variant="h6" color="textSecondary">
                Average Risk Score
              </Typography>
              <Typography variant="h4">
                {(reportData.summary?.averageRiskScore || 0).toFixed(1)}
              </Typography>
            </CardContent>
          </Card>
        </Grid>
      </Grid>

      {/* Charts */}
      <Grid container spacing={3}>
        {/* Activity Timeline */}
        <Grid item xs={12} md={8}>
          <Paper sx={{ p: 2 }}>
            <Typography variant="h6" gutterBottom>
              Activity Timeline
            </Typography>
            <ResponsiveContainer width="100%" height={300}>
              <LineChart data={reportData.groupedActivities || reportData.dailyActivities || []}>
                <CartesianGrid strokeDasharray="3 3" />
                <XAxis dataKey="date" />
                <YAxis />
                <Tooltip />
                <Legend />
                <Line type="monotone" dataKey="count" stroke="#8884d8" name="Activities" />
                <Line type="monotone" dataKey="riskScore" stroke="#82ca9d" name="Avg Risk Score" />
              </LineChart>
            </ResponsiveContainer>
          </Paper>
        </Grid>

        {/* Activity Types Pie Chart */}
        <Grid item xs={12} md={4}>
          <Paper sx={{ p: 2 }}>
            <Typography variant="h6" gutterBottom>
              Activity Types
            </Typography>
            <ResponsiveContainer width="100%" height={300}>
              <PieChart>
                <Pie
                  data={reportData.activityTypes || []}
                  cx="50%"
                  cy="50%"
                  labelLine={false}
                  label={({ name, percent }) => `${name} ${(percent * 100).toFixed(0)}%`}
                  outerRadius={80}
                  fill="#8884d8"
                  dataKey="count"
                >
                  {(reportData.activityTypes || []).map((entry, index) => (
                    <Cell key={`cell-${index}`} fill={COLORS[index % COLORS.length]} />
                  ))}
                </Pie>
                <Tooltip />
              </PieChart>
            </ResponsiveContainer>
          </Paper>
        </Grid>

        {/* Anomaly Types Bar Chart */}
        <Grid item xs={12} md={6}>
          <Paper sx={{ p: 2 }}>
            <Typography variant="h6" gutterBottom>
              Anomaly Types
            </Typography>
            <ResponsiveContainer width="100%" height={300}>
              <BarChart data={reportData.anomalyTypes || []}>
                <CartesianGrid strokeDasharray="3 3" />
                <XAxis dataKey="type" />
                <YAxis />
                <Tooltip />
                <Legend />
                <Bar dataKey="count" fill="#8884d8" />
              </BarChart>
            </ResponsiveContainer>
          </Paper>
        </Grid>

        {/* Top Computers Bar Chart */}
        <Grid item xs={12} md={6}>
          <Paper sx={{ p: 2 }}>
            <Typography variant="h6" gutterBottom>
              Top Computers by Activity
            </Typography>
            <ResponsiveContainer width="100%" height={300}>
              <BarChart data={reportData.topComputers || []}>
                <CartesianGrid strokeDasharray="3 3" />
                <XAxis dataKey="computerName" />
                <YAxis />
                <Tooltip />
                <Legend />
                <Bar dataKey="count" fill="#82ca9d" />
              </BarChart>
            </ResponsiveContainer>
          </Paper>
        </Grid>
      </Grid>
    </Box>
  );
};

export default Analytics;