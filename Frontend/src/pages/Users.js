import React, { useState, useEffect } from 'react';
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
  Pagination
} from '@mui/material';
import {
  Add,
  Search,
  MoreVert,
  Edit,
  Delete,
  Computer,
  Person,
  Email,
  Phone
} from '@mui/icons-material';
import { useAuth } from '../contexts/AuthContext';
import axios from 'axios';

const Users = () => {
  const { user } = useAuth();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [users, setUsers] = useState([]);
  const [searchTerm, setSearchTerm] = useState('');
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [selectedUser, setSelectedUser] = useState(null);
  const [dialogOpen, setDialogOpen] = useState(false);
  const [deleteDialogOpen, setDeleteDialogOpen] = useState(false);
  const [anchorEl, setAnchorEl] = useState(null);
  const [formData, setFormData] = useState({
    username: '',
    email: '',
    fullName: '',
    phone: '',
    department: '',
    position: '',
    role: 'User'
  });

  useEffect(() => {
    fetchUsers();
  }, [page, searchTerm]);

  const fetchUsers = async () => {
    try {
      setLoading(true);
      
      // Mock data for demonstration
      const mockUsers = [
        { 
          id: 1, 
          username: 'john.doe', 
          email: 'john.doe@company.com', 
          fullName: 'John Doe', 
          phone: '+1234567890', 
          department: 'IT', 
          position: 'Developer', 
          role: 'Admin',
          status: 'Active',
          lastLogin: '2024-01-15 10:30:00',
          computerCount: 2
        },
        { 
          id: 2, 
          username: 'jane.smith', 
          email: 'jane.smith@company.com', 
          fullName: 'Jane Smith', 
          phone: '+1234567891', 
          department: 'HR', 
          position: 'Manager', 
          role: 'User',
          status: 'Active',
          lastLogin: '2024-01-15 09:45:00',
          computerCount: 1
        },
        { 
          id: 3, 
          username: 'bob.johnson', 
          email: 'bob.johnson@company.com', 
          fullName: 'Bob Johnson', 
          phone: '+1234567892', 
          department: 'Finance', 
          position: 'Analyst', 
          role: 'User',
          status: 'Inactive',
          lastLogin: '2024-01-14 16:20:00',
          computerCount: 1
        },
        { 
          id: 4, 
          username: 'alice.brown', 
          email: 'alice.brown@company.com', 
          fullName: 'Alice Brown', 
          phone: '+1234567893', 
          department: 'IT', 
          position: 'System Administrator', 
          role: 'Admin',
          status: 'Active',
          lastLogin: '2024-01-15 11:15:00',
          computerCount: 3
        },
        { 
          id: 5, 
          username: 'charlie.wilson', 
          email: 'charlie.wilson@company.com', 
          fullName: 'Charlie Wilson', 
          phone: '+1234567894', 
          department: 'Marketing', 
          position: 'Specialist', 
          role: 'User',
          status: 'Active',
          lastLogin: '2024-01-15 08:30:00',
          computerCount: 1
        }
      ];
      
      setUsers(mockUsers);
      setTotalPages(1);
    } catch (err) {
      setError('Failed to load users');
      console.error('Users fetch error:', err);
    } finally {
      setLoading(false);
    }
  };

  const handleSearch = (event) => {
    setSearchTerm(event.target.value);
    setPage(1);
  };

  const handleMenuClick = (event, user) => {
    setAnchorEl(event.currentTarget);
    setSelectedUser(user);
  };

  const handleMenuClose = () => {
    setAnchorEl(null);
    setSelectedUser(null);
  };

  const handleEdit = () => {
    if (selectedUser) {
      setFormData({
        username: selectedUser.username,
        email: selectedUser.email,
        fullName: selectedUser.fullName,
        phone: selectedUser.phone,
        department: selectedUser.department,
        position: selectedUser.position,
        role: selectedUser.role
      });
      setDialogOpen(true);
    }
    handleMenuClose();
  };

  const handleDelete = () => {
    setDeleteDialogOpen(true);
    handleMenuClose();
  };

  const handleAddUser = () => {
    setFormData({
      username: '',
      email: '',
      fullName: '',
      phone: '',
      department: '',
      position: '',
      role: 'User'
    });
    setDialogOpen(true);
  };

  const handleSaveUser = async () => {
    try {
      // In a real application, this would be an API call
      console.log('Saving user:', formData);
      
      // For demo purposes, we'll just close the dialog
      setDialogOpen(false);
      fetchUsers(); // Refresh the list
    } catch (err) {
      setError('Failed to save user');
      console.error('Save user error:', err);
    }
  };

  const handleConfirmDelete = async () => {
    try {
      // In a real application, this would be an API call
      console.log('Deleting user:', selectedUser?.id);
      
      setDeleteDialogOpen(false);
      fetchUsers(); // Refresh the list
    } catch (err) {
      setError('Failed to delete user');
      console.error('Delete user error:', err);
    }
  };

  const getRoleColor = (role) => {
    switch (role) {
      case 'Admin': return 'error';
      case 'Manager': return 'warning';
      default: return 'primary';
    }
  };

  const getStatusColor = (status) => {
    return status === 'Active' ? 'success' : 'default';
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
        <Typography variant="h4">User Management</Typography>
        <Button
          variant="contained"
          startIcon={<Add />}
          onClick={handleAddUser}
        >
          Add User
        </Button>
      </Box>

      {error && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {error}
        </Alert>
      )}

      <Card sx={{ mb: 3 }}>
        <CardContent>
          <TextField
            fullWidth
            placeholder="Search users..."
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
                <TableCell>Contact</TableCell>
                <TableCell>Department</TableCell>
                <TableCell>Role</TableCell>
                <TableCell>Status</TableCell>
                <TableCell>Computers</TableCell>
                <TableCell>Last Login</TableCell>
                <TableCell>Actions</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {users.map((user) => (
                <TableRow key={user.id}>
                  <TableCell>
                    <Box display="flex" alignItems="center">
                      <Person sx={{ mr: 2, color: 'text.secondary' }} />
                      <Box>
                        <Typography variant="subtitle2">{user.fullName}</Typography>
                        <Typography variant="body2" color="textSecondary">
                          @{user.username}
                        </Typography>
                      </Box>
                    </Box>
                  </TableCell>
                  <TableCell>
                    <Box>
                      <Box display="flex" alignItems="center" mb={0.5}>
                        <Email sx={{ mr: 1, fontSize: 16, color: 'text.secondary' }} />
                        <Typography variant="body2">{user.email}</Typography>
                      </Box>
                      <Box display="flex" alignItems="center">
                        <Phone sx={{ mr: 1, fontSize: 16, color: 'text.secondary' }} />
                        <Typography variant="body2">{user.phone}</Typography>
                      </Box>
                    </Box>
                  </TableCell>
                  <TableCell>
                    <Box>
                      <Typography variant="body2">{user.department}</Typography>
                      <Typography variant="caption" color="textSecondary">
                        {user.position}
                      </Typography>
                    </Box>
                  </TableCell>
                  <TableCell>
                    <Chip 
                      label={user.role} 
                      color={getRoleColor(user.role)}
                      size="small"
                    />
                  </TableCell>
                  <TableCell>
                    <Chip 
                      label={user.status} 
                      color={getStatusColor(user.status)}
                      size="small"
                    />
                  </TableCell>
                  <TableCell>
                    <Box display="flex" alignItems="center">
                      <Computer sx={{ mr: 1, fontSize: 16, color: 'text.secondary' }} />
                      <Typography variant="body2">{user.computerCount}</Typography>
                    </Box>
                  </TableCell>
                  <TableCell>
                    <Typography variant="body2">{user.lastLogin}</Typography>
                  </TableCell>
                  <TableCell>
                    <IconButton
                      onClick={(e) => handleMenuClick(e, user)}
                      size="small"
                    >
                      <MoreVert />
                    </IconButton>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </TableContainer>
        
        <Box display="flex" justifyContent="center" p={2}>
          <Pagination
            count={totalPages}
            page={page}
            onChange={(event, value) => setPage(value)}
            color="primary"
          />
        </Box>
      </Paper>

      {/* User Form Dialog */}
      <Dialog open={dialogOpen} onClose={() => setDialogOpen(false)} maxWidth="md" fullWidth>
        <DialogTitle>
          {selectedUser ? 'Edit User' : 'Add New User'}
        </DialogTitle>
        <DialogContent>
          <Grid container spacing={2} sx={{ mt: 1 }}>
            <Grid item xs={12} sm={6}>
              <TextField
                fullWidth
                label="Username"
                value={formData.username}
                onChange={(e) => setFormData({ ...formData, username: e.target.value })}
              />
            </Grid>
            <Grid item xs={12} sm={6}>
              <TextField
                fullWidth
                label="Full Name"
                value={formData.fullName}
                onChange={(e) => setFormData({ ...formData, fullName: e.target.value })}
              />
            </Grid>
            <Grid item xs={12} sm={6}>
              <TextField
                fullWidth
                label="Email"
                type="email"
                value={formData.email}
                onChange={(e) => setFormData({ ...formData, email: e.target.value })}
              />
            </Grid>
            <Grid item xs={12} sm={6}>
              <TextField
                fullWidth
                label="Phone"
                value={formData.phone}
                onChange={(e) => setFormData({ ...formData, phone: e.target.value })}
              />
            </Grid>
            <Grid item xs={12} sm={6}>
              <TextField
                fullWidth
                label="Department"
                value={formData.department}
                onChange={(e) => setFormData({ ...formData, department: e.target.value })}
              />
            </Grid>
            <Grid item xs={12} sm={6}>
              <TextField
                fullWidth
                label="Position"
                value={formData.position}
                onChange={(e) => setFormData({ ...formData, position: e.target.value })}
              />
            </Grid>
            <Grid item xs={12} sm={6}>
              <TextField
                fullWidth
                select
                label="Role"
                value={formData.role}
                onChange={(e) => setFormData({ ...formData, role: e.target.value })}
                SelectProps={{ native: true }}
              >
                <option value="User">User</option>
                <option value="Manager">Manager</option>
                <option value="Admin">Admin</option>
              </TextField>
            </Grid>
          </Grid>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setDialogOpen(false)}>Cancel</Button>
          <Button onClick={handleSaveUser} variant="contained">Save</Button>
        </DialogActions>
      </Dialog>

      {/* Delete Confirmation Dialog */}
      <Dialog open={deleteDialogOpen} onClose={() => setDeleteDialogOpen(false)}>
        <DialogTitle>Confirm Delete</DialogTitle>
        <DialogContent>
          <Typography>
            Are you sure you want to delete user "{selectedUser?.fullName}"? This action cannot be undone.
          </Typography>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setDeleteDialogOpen(false)}>Cancel</Button>
          <Button onClick={handleConfirmDelete} color="error" variant="contained">
            Delete
          </Button>
        </DialogActions>
      </Dialog>

      {/* Context Menu */}
      <Menu
        anchorEl={anchorEl}
        open={Boolean(anchorEl)}
        onClose={handleMenuClose}
      >
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