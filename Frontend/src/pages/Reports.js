import React, { useState, useEffect } from 'react';
import {
  Box,
  Card,
  CardContent,
  Typography,
  Paper,
  Grid,
  Button,
  Select,
  MenuItem,
  FormControl,
  InputLabel,
  TextField,
  DatePicker,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Chip,
  Dialog,
  DialogTitle,
  DialogContent,
  DialogActions,
  CircularProgress,
  Alert,
  Tabs,
  Tab
} from '@mui/material';
import {
  Download,
  FilterList,
  Assessment,
  Timeline,
  PieChart,
  BarChart,
  FileDownload,
  DateRange
} from '@mui/icons-material';
import { LineChart, Line, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer, PieChart as RePieChart, Pie, Cell, BarChart as ReBarChart, Bar } from 'recharts';
import { useAuth } from '../contexts/AuthContext';
import axios from 'axios';

const Reports = () => {
  const { user } = useAuth();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [tabValue, setTabValue] = useState(0);
  const [reportType, setReportType] = useState('daily');
  const [dateRange, setDateRange] = useState({ start: null, end: null });
  const [exportDialogOpen, setExportDialogOpen] = useState(false);
  const [exportFormat, setExportFormat] = useState('pdf');
  
  // Mock data for charts
  const [activityData, setActivityData] = useState([
    { date: '2024-01-10', activities: 120, anomalies: 2 },
    { date: '2024-01-11', activities: 145, anomalies: 1 },
    { date: '2024-01-12', activities: 132, anomalies: 3 },
    { date: '2024-01-13', activities: 156, anomalies: 0 },
    { date: '2024-01-14', activities: 178, anomalies: 4 },
    { date: '2024-01-15', activities: 165, anomalies: 2 }
  ]);

  const [departmentData, setDepartmentData] = useState([
    { name: 'IT', value: 45, color: '#1976d2' },
    { name: 'HR', value: 25, color: '#388e3c' },
    { name: 'Finance', value: 30, color: '#f57c00' },
    { name: 'Marketing', value: 20, color: '#7b1fa2' },
    { name: 'Operations', value: 35, color: '#d32f2f' }
  ]);

  const [userActivityData, setUserActivityData] = useState([
    { name: 'John Doe', activities: 89 },
    { name: 'Jane Smith', activities: 76 },
    { name: 'Bob Johnson', activities: 65 },
    { name: 'Alice Brown', activities: 92 },
    { name: 'Charlie Wilson', activities: 54 }
  ]);

  const [reports, setReports] = useState([
    { 
      id: 1, 
      type: 'Daily Report', 
      generatedDate: '2024-01-15 08:00:00', 
      period: '2024-01-14', 
      status: 'Completed',
      fileSize: '2.3 MB'
    },
    { 
      id: 2, 
      type: 'Weekly Report', 
      generatedDate: '2024-01-15 09:30:00', 
      period: '2024-01-08 - 2024-01-14', 
      status: 'Completed',
      fileSize: '5.7 MB'
    },
    { 
      id: 3, 
      type: 'User Activity Report', 
      generatedDate: '2024-01-14 16:45:00', 
      period: '2024-01-14', 
      status: 'Completed',
      fileSize: '1.8 MB'
    }
  ]);

  useEffect(() => {
    fetchReportData();
  }, [tabValue, reportType]);

  const fetchReportData = async () => {
    try {
      setLoading(true);
      // In a real application, this would fetch data from the API
      // For now, we're using mock data
      await new Promise(resolve => setTimeout(resolve, 1000));
    } catch (err) {
      setError('Failed to load report data');
      console.error('Reports fetch error:', err);
    } finally {
      setLoading(false);
    }
  };

  const handleTabChange = (event, newValue) => {
    setTabValue(newValue);
  };

  const handleExport = () => {
    setExportDialogOpen(true);
  };

  const handleConfirmExport = async () => {
    try {
      // In a real application, this would trigger an export API call
      console.log(`Exporting report in ${exportFormat} format`);
      setExportDialogOpen(false);
    } catch (err) {
      setError('Failed to export report');
      console.error('Export error:', err);
    }
  };

  const getStatusColor = (status) => {
    return status === 'Completed' ? 'success' : 'warning';
  };

  if (loading) {
    return (
      <Box display="flex" justifyContent="center" alignItems="center" minHeight="60vh">
        <CircularProgress />
      </Box>
    );
  }

  return (
    <Box>
      <Box display="flex" justifyContent="space-between" alignItems="center" mb={3}>
        <Typography variant="h4">Reports & Analytics</Typography>
        <Button
          variant="contained"
          startIcon={<Download />}
          onClick={handleExport}
        >
          Export Report
        </Button>
      </Box>

      {error && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {error}
        </Alert>
      )}

      <Tabs value={tabValue} onChange={handleTabChange} sx={{ mb: 3 }}>
        <Tab label="Overview" icon={<Assessment />} />
        <Tab label="Activity Trends" icon={<Timeline />} />
        <Tab label="Department Analysis" icon={<PieChart />} />
        <Tab label="User Statistics" icon={<BarChart />} />
        <Tab label="Generated Reports" icon={<FileDownload />} />
      </Tabs>

      {/* Overview Tab */}
      {tabValue === 0 && (
        <Grid container spacing={3}>
          <Grid item xs={12} md={6}>
            <Card>
              <CardContent>
                <Typography variant="h6" gutterBottom>
                  Activity Overview
                </Typography>
                <ResponsiveContainer width="100%" height={300}>
                  <LineChart data={activityData}>
                    <CartesianGrid strokeDasharray="3 3" />
                    <XAxis dataKey="date" />
                    <YAxis />
                    <Tooltip />
                    <Line type="monotone" dataKey="activities" stroke="#1976d2" strokeWidth={2} />
                    <Line type="monotone" dataKey="anomalies" stroke="#f44336" strokeWidth={2} />
                  </LineChart>
                </ResponsiveContainer>
              </CardContent>
            </Card>
          </Grid>
          
          <Grid item xs={12} md={6}>
            <Card>
              <CardContent>
                <Typography variant="h6" gutterBottom>
                  Department Distribution
                </Typography>
                <ResponsiveContainer width="100%" height={300}>
                  <RePieChart>
                    <Pie
                      data={departmentData}
                      cx="50%"
                      cy="50%"
                      outerRadius={80}
                      fill="#8884d8"
                      dataKey="value"
                      label
                    >
                      {departmentData.map((entry, index) => (
                        <Cell key={`cell-${index}`} fill={entry.color} />
                      ))}
                    </Pie>
                    <Tooltip />
                  </RePieChart>
                </ResponsiveContainer>
              </CardContent>
            </Card>
          </Grid>
        </Grid>
      )}

      {/* Activity Trends Tab */}
      {tabValue === 1 && (
        <Card>
          <CardContent>
            <Box display="flex" justifyContent="space-between" alignItems="center" mb={3}>
              <Typography variant="h6">Activity Trends Analysis</Typography>
              <FormControl size="small" sx={{ minWidth: 120 }}>
                <InputLabel>Period</InputLabel>
                <Select
                  value={reportType}
                  label="Period"
                  onChange={(e) => setReportType(e.target.value)}
                >
                  <MenuItem value="daily">Daily</MenuItem>
                  <MenuItem value="weekly">Weekly</MenuItem>
                  <MenuItem value="monthly">Monthly</MenuItem>
                </Select>
              </FormControl>
            </Box>
            <ResponsiveContainer width="100%" height={400}>
              <LineChart data={activityData}>
                <CartesianGrid strokeDasharray="3 3" />
                <XAxis dataKey="date" />
                <YAxis />
                <Tooltip />
                <Line type="monotone" dataKey="activities" stroke="#1976d2" strokeWidth={2} name="Activities" />
                <Line type="monotone" dataKey="anomalies" stroke="#f44336" strokeWidth={2} name="Anomalies" />
              </LineChart>
            </ResponsiveContainer>
          </CardContent>
        </Card>
      )}

      {/* Department Analysis Tab */}
      {tabValue === 2 && (
        <Grid container spacing={3}>
          <Grid item xs={12} md={6}>
            <Card>
              <CardContent>
                <Typography variant="h6" gutterBottom>
                  Department Activity Distribution
                </Typography>
                <ResponsiveContainer width="100%" height={300}>
                  <RePieChart>
                    <Pie
                      data={departmentData}
                      cx="50%"
                      cy="50%"
                      outerRadius={100}
                      fill="#8884d8"
                      dataKey="value"
                      label={({name, value}) => `${name}: ${value}`}
                    >
                      {departmentData.map((entry, index) => (
                        <Cell key={`cell-${index}`} fill={entry.color} />
                      ))}
                    </Pie>
                    <Tooltip />
                  </RePieChart>
                </ResponsiveContainer>
              </CardContent>
            </Card>
          </Grid>
          
          <Grid item xs={12} md={6}>
            <Card>
              <CardContent>
                <Typography variant="h6" gutterBottom>
                  Department Statistics
                </Typography>
                <TableContainer>
                  <Table>
                    <TableHead>
                      <TableRow>
                        <TableCell>Department</TableCell>
                        <TableCell align="right">Activities</TableCell>
                        <TableCell align="right">Users</TableCell>
                        <TableCell align="right">Anomalies</TableCell>
                      </TableRow>
                    </TableHead>
                    <TableBody>
                      {departmentData.map((dept) => (
                        <TableRow key={dept.name}>
                          <TableCell>
                            <Chip 
                              label={dept.name} 
                              size="small" 
                              style={{ backgroundColor: dept.color, color: 'white' }}
                            />
                          </TableCell>
                          <TableCell align="right">{dept.value}</TableCell>
                          <TableCell align="right">{Math.floor(dept.value / 3)}</TableCell>
                          <TableCell align="right">{Math.floor(Math.random() * 5)}</TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </TableContainer>
              </CardContent>
            </Card>
          </Grid>
        </Grid>
      )}

      {/* User Statistics Tab */}
      {tabValue === 3 && (
        <Card>
          <CardContent>
            <Typography variant="h6" gutterBottom>
              User Activity Statistics
            </Typography>
            <ResponsiveContainer width="100%" height={400}>
              <ReBarChart data={userActivityData}>
                <CartesianGrid strokeDasharray="3 3" />
                <XAxis dataKey="name" />
                <YAxis />
                <Tooltip />
                <Bar dataKey="activities" fill="#1976d2" />
              </ReBarChart>
            </ResponsiveContainer>
          </CardContent>
        </Card>
      )}

      {/* Generated Reports Tab */}
      {tabValue === 4 && (
        <Card>
          <CardContent>
            <Box display="flex" justifyContent="space-between" alignItems="center" mb={3}>
              <Typography variant="h6">Generated Reports</Typography>
              <Button
                variant="outlined"
                startIcon={<FilterList />}
                size="small"
              >
                Filter
              </Button>
            </Box>
            <TableContainer>
              <Table>
                <TableHead>
                  <TableRow>
                    <TableCell>Report Type</TableCell>
                    <TableCell>Generated Date</TableCell>
                    <TableCell>Period</TableCell>
                    <TableCell>Status</TableCell>
                    <TableCell>File Size</TableCell>
                    <TableCell>Actions</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {reports.map((report) => (
                    <TableRow key={report.id}>
                      <TableCell>{report.type}</TableCell>
                      <TableCell>{report.generatedDate}</TableCell>
                      <TableCell>{report.period}</TableCell>
                      <TableCell>
                        <Chip 
                          label={report.status} 
                          color={getStatusColor(report.status)}
                          size="small"
                        />
                      </TableCell>
                      <TableCell>{report.fileSize}</TableCell>
                      <TableCell>
                        <Button
                          size="small"
                          startIcon={<FileDownload />}
                          variant="outlined"
                        >
                          Download
                        </Button>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </TableContainer>
          </CardContent>
        </Card>
      )}

      {/* Export Dialog */}
      <Dialog open={exportDialogOpen} onClose={() => setExportDialogOpen(false)} maxWidth="sm" fullWidth>
        <DialogTitle>Export Report</DialogTitle>
        <DialogContent>
          <Grid container spacing={2} sx={{ mt: 1 }}>
            <Grid item xs={12}>
              <FormControl fullWidth>
                <InputLabel>Export Format</InputLabel>
                <Select
                  value={exportFormat}
                  label="Export Format"
                  onChange={(e) => setExportFormat(e.target.value)}
                >
                  <MenuItem value="pdf">PDF</MenuItem>
                  <MenuItem value="excel">Excel</MenuItem>
                  <MenuItem value="csv">CSV</MenuItem>
                </Select>
              </FormControl>
            </Grid>
            <Grid item xs={12}>
              <TextField
                fullWidth
                label="Date Range"
                type="text"
                placeholder="Select date range"
                InputProps={{
                  startAdornment: <DateRange sx={{ mr: 1, color: 'text.secondary' }} />
                }}
              />
            </Grid>
          </Grid>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setExportDialogOpen(false)}>Cancel</Button>
          <Button onClick={handleConfirmExport} variant="contained">Export</Button>
        </DialogActions>
      </Dialog>
    </Box>
  );
};

export default Reports;