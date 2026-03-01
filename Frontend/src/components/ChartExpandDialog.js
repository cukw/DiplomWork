import React from 'react';
import {
  Box,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
} from '@mui/material';

const ChartExpandDialog = ({ open, onClose, title, children }) => (
  <Dialog open={open} onClose={onClose} maxWidth="xl" fullWidth>
    <DialogTitle>{title}</DialogTitle>
    <DialogContent>
      <Box sx={{ width: '100%', height: '70vh', minHeight: 360 }}>
        {children}
      </Box>
    </DialogContent>
    <DialogActions>
      <Button onClick={onClose}>Закрыть</Button>
    </DialogActions>
  </Dialog>
);

export default ChartExpandDialog;
