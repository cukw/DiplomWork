import React, { useEffect, useMemo, useState } from 'react';
import {
  Box,
  Card,
  CardContent,
  Typography,
  Paper,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Button,
  Chip,
  Dialog,
  DialogTitle,
  DialogContent,
  DialogActions,
  TextField,
  Grid,
  IconButton,
  Menu,
  MenuItem,
  Alert,
  CircularProgress,
  InputAdornment,
  Pagination,
  Stack,
} from '@mui/material';
import {
  Add,
  Search,
  MoreVert,
  Edit,
  Delete,
  Computer,
  Person,
  Lan,
} from '@mui/icons-material';
import { userAPI } from '../services/api';

const PAGE_SIZE = 10;
const FETCH_PAGE_SIZE = 500;

const emptyForm = {
  authUserId: '',
  fullName: '',
  department: '',
  hostname: '',
  osVersion: '',
  ipAddress: '',
  macAddress: '',
};

const formatDateTime = (value) => {
  if (!value) return '-';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? String(value) : date.toLocaleString();
};

const normalizeUsers = (rows) => (rows || []).map((u) => ({
  id: u.id,
  authUserId: u.authUserId,
  fullName: u.fullName || 'Unnamed user',
  department: u.department || 'Unassigned',
  createdAt: u.createdAt,
  computer: u.computer || null,
}));

const Users = () => {
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState(null);
  const [success, setSuccess] = useState(null);
  const [users, setUsers] = useState([]);
  const [searchTerm, setSearchTerm] = useState('');
  const [page, setPage] = useState(1);

  const [selectedUser, setSelectedUser] = useState(null);
  const [dialogOpen, setDialogOpen] = useState(false);
  const [deleteDialogOpen, setDeleteDialogOpen] = useState(false);
  const [anchorEl, setAnchorEl] = useState(null);
  const [formData, setFormData] = useState(emptyForm);

  const fetchUsers = async ({ silent = false } = {}) => {
    try {
      if (!silent) setLoading(true);
      setError(null);

      const response = await userAPI.getUsers({ page: 1, pageSize: FETCH_PAGE_SIZE });
      setUsers(normalizeUsers(response?.users || []));
    } catch (err) {
      setError(err?.response?.data?.message || err?.message || 'Failed to load users');
      console.error('Users fetch error:', err);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchUsers();
  }, []);

  useEffect(() => {
    setPage(1);
  }, [searchTerm]);

  const filteredUsers = useMemo(() => {
    const query = searchTerm.trim().toLowerCase();
    if (!query) return users;

    return users.filter((user) => {
      const haystack = [
        user.fullName,
        user.department,
        String(user.id ?? ''),
        String(user.authUserId ?? ''),
        user.computer?.hostname,
        user.computer?.ipAddress,
        user.computer?.macAddress,
        user.computer?.status,
      ]
        .filter(Boolean)
        .join(' ')
        .toLowerCase();

      return haystack.includes(query);
    });
  }, [users, searchTerm]);

  const totalPages = Math.max(1, Math.ceil(filteredUsers.length / PAGE_SIZE));
  const safePage = Math.min(page, totalPages);
  const pagedUsers = filteredUsers.slice((safePage - 1) * PAGE_SIZE, safePage * PAGE_SIZE);

  const resetForm = () => setFormData(emptyForm);

  const handleSearch = (event) => {
    setSearchTerm(event.target.value);
  };

  const handleMenuClick = (event, user) => {
    setAnchorEl(event.currentTarget);
    setSelectedUser(user);
  };

  const handleMenuClose = () => {
    setAnchorEl(null);
  };

  const handleAddUser = () => {
    setSelectedUser(null);
    resetForm();
    setDialogOpen(true);
  };

  const handleEdit = () => {
    if (!selectedUser) return;

    setFormData({
      authUserId: String(selectedUser.authUserId ?? ''),
      fullName: selectedUser.fullName || '',
      department: selectedUser.department || '',
      hostname: selectedUser.computer?.hostname || '',
      osVersion: selectedUser.computer?.osVersion || '',
      ipAddress: selectedUser.computer?.ipAddress || '',
      macAddress: selectedUser.computer?.macAddress || '',
    });
    setDialogOpen(true);
    handleMenuClose();
  };

  const handleDelete = () => {
    if (!selectedUser) return;
    setDeleteDialogOpen(true);
    handleMenuClose();
  };

  const handleSaveUser = async () => {
    try {
      setSaving(true);
      setError(null);
      setSuccess(null);

      const fullName = String(formData.fullName || '').trim();
      const department = String(formData.department || '').trim();

      if (!fullName) {
        setError('Full Name is required');
        return;
      }
      if (!department) {
        setError('Department is required');
        return;
      }

      if (selectedUser?.id) {
        await userAPI.updateUser(selectedUser.id, {
          fullName,
          department,
        });
        setSuccess('User updated successfully');
      } else {
        const authUserId = Number(formData.authUserId);
        if (!Number.isFinite(authUserId) || authUserId <= 0) {
          setError('Auth User ID must be a positive number');
          return;
        }

        await userAPI.createUser({
          authUserId,
          fullName,
          department,
          hostname: String(formData.hostname || '').trim(),
          osVersion: String(formData.osVersion || '').trim(),
          ipAddress: String(formData.ipAddress || '').trim(),
          macAddress: String(formData.macAddress || '').trim(),
        });
        setSuccess('User created successfully');
      }

      setDialogOpen(false);
      setSelectedUser(null);
      resetForm();
      await fetchUsers({ silent: true });
      setTimeout(() => setSuccess(null), 3000);
    } catch (err) {
      setError(err?.response?.data?.message || err?.message || 'Failed to save user');
      console.error('Save user error:', err);
    } finally {
      setSaving(false);
    }
  };

  const handleConfirmDelete = async () => {
    if (!selectedUser?.id) return;

    try {
      setSaving(true);
      setError(null);
      setSuccess(null);

      await userAPI.deleteUser(selectedUser.id);
      setSuccess(`User "${selectedUser.fullName}" deleted`);
      setDeleteDialogOpen(false);
      setSelectedUser(null);
      await fetchUsers({ silent: true });
      setTimeout(() => setSuccess(null), 3000);
    } catch (err) {
      setError(err?.response?.data?.message || err?.message || 'Failed to delete user');
      console.error('Delete user error:', err);
    } finally {
      setSaving(false);
    }
  };

  const getComputerStatusColor = (status) => {
    const normalized = String(status || '').toLowerCase();
    if (normalized.includes('online') || normalized.includes('active')) return 'success';
    if (normalized.includes('offline')) return 'warning';
    return 'default';
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
      <Box display="flex" justifyContent="space-between" alignItems="center" mb={3} gap={2} flexWrap="wrap">
        <Box>
          <Typography variant="h4">User Management</Typography>
          <Typography variant="body2" color="text.secondary">
            Real CRUD via UserService (gateway `/api/user/users`)
          </Typography>
        </Box>
        <Button variant="contained" startIcon={<Add />} onClick={handleAddUser}>
          Add User
        </Button>
      </Box>

      {error && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {error}
        </Alert>
      )}

      {success && (
        <Alert severity="success" sx={{ mb: 2 }}>
          {success}
        </Alert>
      )}

      <Card sx={{ mb: 3 }}>
        <CardContent>
          <TextField
            fullWidth
            placeholder="Search by name, department, computer, IP, MAC, auth ID..."
            value={searchTerm}
            onChange={handleSearch}
            InputProps={{
              startAdornment: (
                <InputAdornment position="start">
                  <Search />
                </InputAdornment>
              ),
            }}
          />
        </CardContent>
      </Card>

      <Paper>
        <TableContainer>
          <Table>
            <TableHead>
              <TableRow>
                <TableCell>User</TableCell>
                <TableCell>Department</TableCell>
                <TableCell>Computer</TableCell>
                <TableCell>Network</TableCell>
                <TableCell>Created</TableCell>
                <TableCell align="right">Actions</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {pagedUsers.length === 0 ? (
                <TableRow>
                  <TableCell colSpan={6} align="center">
                    No users found
                  </TableCell>
                </TableRow>
              ) : pagedUsers.map((row) => (
                <TableRow key={row.id} hover>
                  <TableCell>
                    <Box display="flex" alignItems="center" gap={1.5}>
                      <Person sx={{ color: 'text.secondary' }} />
                      <Box>
                        <Typography variant="subtitle2">{row.fullName}</Typography>
                        <Typography variant="body2" color="text.secondary">
                          User #{row.id} · Auth #{row.authUserId || '-'}
                        </Typography>
                      </Box>
                    </Box>
                  </TableCell>
                  <TableCell>{row.department}</TableCell>
                  <TableCell>
                    {row.computer ? (
                      <Stack spacing={0.5}>
                        <Box display="flex" alignItems="center" gap={1}>
                          <Computer sx={{ fontSize: 16, color: 'text.secondary' }} />
                          <Typography variant="body2">{row.computer.hostname || '-'}</Typography>
                        </Box>
                        <Chip
                          size="small"
                          label={(row.computer.status || 'unknown').toUpperCase()}
                          color={getComputerStatusColor(row.computer.status)}
                          sx={{ width: 'fit-content' }}
                        />
                      </Stack>
                    ) : (
                      <Typography variant="body2" color="text.secondary">No computer</Typography>
                    )}
                  </TableCell>
                  <TableCell>
                    {row.computer ? (
                      <Stack spacing={0.5}>
                        <Box display="flex" alignItems="center" gap={1}>
                          <Lan sx={{ fontSize: 16, color: 'text.secondary' }} />
                          <Typography variant="body2">{row.computer.ipAddress || '-'}</Typography>
                        </Box>
                        <Typography variant="caption" color="text.secondary">
                          {row.computer.macAddress || '-'}
                        </Typography>
                      </Stack>
                    ) : '-'}
                  </TableCell>
                  <TableCell>
                    <Typography variant="body2">{formatDateTime(row.createdAt)}</Typography>
                    {row.computer?.lastSeen && (
                      <Typography variant="caption" color="text.secondary" display="block">
                        Last seen: {formatDateTime(row.computer.lastSeen)}
                      </Typography>
                    )}
                  </TableCell>
                  <TableCell align="right">
                    <IconButton onClick={(e) => handleMenuClick(e, row)} size="small">
                      <MoreVert />
                    </IconButton>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </TableContainer>

        <Box display="flex" justifyContent="space-between" alignItems="center" p={2} flexWrap="wrap" gap={1}>
          <Typography variant="body2" color="text.secondary">
            Showing {pagedUsers.length} of {filteredUsers.length} loaded users
          </Typography>
          <Pagination
            count={totalPages}
            page={safePage}
            onChange={(_event, value) => setPage(value)}
            color="primary"
          />
        </Box>
      </Paper>

      <Dialog open={dialogOpen} onClose={() => setDialogOpen(false)} maxWidth="md" fullWidth>
        <DialogTitle>{selectedUser ? 'Edit User' : 'Add New User'}</DialogTitle>
        <DialogContent>
          <Grid container spacing={2} sx={{ mt: 0.5 }}>
            <Grid item xs={12} md={4}>
              <TextField
                fullWidth
                label="Auth User ID"
                type="number"
                value={formData.authUserId}
                onChange={(e) => setFormData((prev) => ({ ...prev, authUserId: e.target.value }))}
                disabled={Boolean(selectedUser)}
                helperText={selectedUser ? 'Auth user link is immutable in current API' : 'Required'}
              />
            </Grid>
            <Grid item xs={12} md={8}>
              <TextField
                fullWidth
                label="Full Name"
                value={formData.fullName}
                onChange={(e) => setFormData((prev) => ({ ...prev, fullName: e.target.value }))}
              />
            </Grid>
            <Grid item xs={12} md={6}>
              <TextField
                fullWidth
                label="Department"
                value={formData.department}
                onChange={(e) => setFormData((prev) => ({ ...prev, department: e.target.value }))}
              />
            </Grid>
            <Grid item xs={12} md={6}>
              <TextField
                fullWidth
                label="Hostname"
                value={formData.hostname}
                onChange={(e) => setFormData((prev) => ({ ...prev, hostname: e.target.value }))}
                disabled={Boolean(selectedUser)}
                helperText={selectedUser ? 'Computer details are managed by current backend separately' : ''}
              />
            </Grid>
            <Grid item xs={12} md={6}>
              <TextField
                fullWidth
                label="OS Version"
                value={formData.osVersion}
                onChange={(e) => setFormData((prev) => ({ ...prev, osVersion: e.target.value }))}
                disabled={Boolean(selectedUser)}
              />
            </Grid>
            <Grid item xs={12} md={6}>
              <TextField
                fullWidth
                label="IP Address"
                value={formData.ipAddress}
                onChange={(e) => setFormData((prev) => ({ ...prev, ipAddress: e.target.value }))}
                disabled={Boolean(selectedUser)}
              />
            </Grid>
            <Grid item xs={12}>
              <TextField
                fullWidth
                label="MAC Address"
                value={formData.macAddress}
                onChange={(e) => setFormData((prev) => ({ ...prev, macAddress: e.target.value }))}
                disabled={Boolean(selectedUser)}
              />
            </Grid>
          </Grid>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setDialogOpen(false)} disabled={saving}>Cancel</Button>
          <Button onClick={handleSaveUser} variant="contained" disabled={saving}>
            {saving ? 'Saving...' : 'Save'}
          </Button>
        </DialogActions>
      </Dialog>

      <Dialog open={deleteDialogOpen} onClose={() => setDeleteDialogOpen(false)}>
        <DialogTitle>Confirm Delete</DialogTitle>
        <DialogContent>
          <Typography>
            Delete user "{selectedUser?.fullName}" (ID {selectedUser?.id})? This action cannot be undone.
          </Typography>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setDeleteDialogOpen(false)} disabled={saving}>Cancel</Button>
          <Button onClick={handleConfirmDelete} color="error" variant="contained" disabled={saving}>
            {saving ? 'Deleting...' : 'Delete'}
          </Button>
        </DialogActions>
      </Dialog>

      <Menu anchorEl={anchorEl} open={Boolean(anchorEl)} onClose={handleMenuClose}>
        <MenuItem onClick={handleEdit}>
          <Edit sx={{ mr: 1 }} fontSize="small" />
          Edit
        </MenuItem>
        <MenuItem onClick={handleDelete}>
          <Delete sx={{ mr: 1 }} fontSize="small" />
          Delete
        </MenuItem>
      </Menu>
    </Box>
  );
};

export default Users;
