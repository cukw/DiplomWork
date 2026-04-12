use pyo3::prelude::*;

#[pyfunction]
fn idle_time_ms() -> PyResult<u64> {
    platform::idle_time_ms()
}

#[pyfunction]
fn active_window_title() -> PyResult<String> {
    platform::active_window_title()
}

#[pyfunction]
fn lock_workstation() -> PyResult<bool> {
    platform::lock_workstation()
}

#[pymodule]
fn agent_sysprobe(_py: Python<'_>, m: &Bound<'_, PyModule>) -> PyResult<()> {
    m.add_function(wrap_pyfunction!(idle_time_ms, m)?)?;
    m.add_function(wrap_pyfunction!(active_window_title, m)?)?;
    m.add_function(wrap_pyfunction!(lock_workstation, m)?)?;
    Ok(())
}

#[cfg(windows)]
mod platform {
    use pyo3::exceptions::PyRuntimeError;
    use pyo3::PyResult;
    use windows_sys::Win32::Foundation::HWND;
    use windows_sys::Win32::UI::Input::KeyboardAndMouse::{GetLastInputInfo, LASTINPUTINFO};
    use windows_sys::Win32::UI::WindowsAndMessaging::{GetForegroundWindow, GetWindowTextLengthW, GetWindowTextW, LockWorkStation};

    pub fn idle_time_ms() -> PyResult<u64> {
        unsafe {
            let mut info = LASTINPUTINFO {
                cbSize: core::mem::size_of::<LASTINPUTINFO>() as u32,
                dwTime: 0,
            };
            let ok = GetLastInputInfo(&mut info as *mut LASTINPUTINFO);
            if ok == 0 {
                return Err(PyRuntimeError::new_err("GetLastInputInfo failed"));
            }
            let tick = windows_sys::Win32::System::SystemInformation::GetTickCount64();
            Ok(tick.saturating_sub(info.dwTime as u64))
        }
    }

    pub fn active_window_title() -> PyResult<String> {
        unsafe {
            let hwnd: HWND = GetForegroundWindow();
            if hwnd == 0 {
                return Ok(String::new());
            }
            let len = GetWindowTextLengthW(hwnd);
            if len <= 0 {
                return Ok(String::new());
            }
            let mut buf = vec![0u16; (len as usize) + 1];
            let copied = GetWindowTextW(hwnd, buf.as_mut_ptr(), buf.len() as i32);
            if copied <= 0 {
                return Ok(String::new());
            }
            Ok(String::from_utf16_lossy(&buf[..copied as usize]))
        }
    }

    pub fn lock_workstation() -> PyResult<bool> {
        unsafe { Ok(LockWorkStation() != 0) }
    }
}

#[cfg(not(windows))]
mod platform {
    use pyo3::PyResult;

    pub fn idle_time_ms() -> PyResult<u64> {
        Ok(0)
    }

    pub fn active_window_title() -> PyResult<String> {
        Ok(String::new())
    }

    pub fn lock_workstation() -> PyResult<bool> {
        Ok(false)
    }
}
