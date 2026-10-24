//! Thin display metadata adapter; WGC capture and monitor handles stay upstream-owned.
use crate::gpu_analysis::Rect;
use serde::Serialize;
use windows::Win32::{
    Devices::Display::*,
    Foundation::{ERROR_INSUFFICIENT_BUFFER, LUID},
    Graphics::Gdi::{GetMonitorInfoW, HMONITOR, MONITORINFO},
    UI::{HiDpi::GetDpiForWindow, WindowsAndMessaging::*},
};
use windows::core::w;
use windows_capture::monitor::Monitor;
type Error = Box<dyn std::error::Error + Send + Sync>;
#[derive(Clone, Serialize)]
#[serde(rename_all = "PascalCase")]
pub struct Display {
    pub display_id: String,
    pub device_name: String,
    pub bounds: Rect,
    pub rotation: i32,
    pub refresh_numerator: u32,
    pub refresh_denominator: u32,
    pub scale_percent: i32,
    pub primary: bool,
    pub identity_quality: String,
    pub advanced_color_state: Option<u32>,
    pub dpi: u32,
    pub adapter_id: String,
    pub target_id: u32,
}
#[derive(Serialize)]
#[serde(rename_all = "PascalCase")]
pub struct Topology {
    pub schema: &'static str,
    pub signature: String,
    pub displays: Vec<Display>,
}
fn text(value: &[u16]) -> String {
    String::from_utf16_lossy(&value[..value.iter().position(|v| *v == 0).unwrap_or(value.len())])
}
fn header<T>(
    kind: DISPLAYCONFIG_DEVICE_INFO_TYPE,
    adapter: LUID,
    id: u32,
) -> DISPLAYCONFIG_DEVICE_INFO_HEADER {
    DISPLAYCONFIG_DEVICE_INFO_HEADER {
        r#type: kind,
        size: std::mem::size_of::<T>() as u32,
        adapterId: adapter,
        id,
    }
}
fn effective_dpi(x: i32, y: i32) -> Result<u32, Error> {
    // A hidden, nonactivating system-class window uses the documented per-monitor DPI API.
    unsafe {
        let window = CreateWindowExW(
            WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW,
            w!("STATIC"),
            w!(""),
            WS_POPUP,
            x,
            y,
            1,
            1,
            None,
            None,
            None,
            None,
        )?;
        let dpi = GetDpiForWindow(window);
        DestroyWindow(window)?;
        if dpi == 0 {
            return Err("Monitor DPI unavailable".into());
        }
        Ok(dpi)
    }
}
pub fn snapshot() -> Result<Topology, Error> {
    unsafe {
        for _ in 0..3 {
            let (mut count, mut mode_count) = (0, 0);
            GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, &mut count, &mut mode_count).ok()?;
            if count > 64 || mode_count > 256 {
                return Err("Topology budget exceeded".into());
            }
            let mut paths = vec![DISPLAYCONFIG_PATH_INFO::default(); count as usize];
            let mut modes = vec![DISPLAYCONFIG_MODE_INFO::default(); mode_count as usize];
            let status = QueryDisplayConfig(
                QDC_ONLY_ACTIVE_PATHS,
                &mut count,
                paths.as_mut_ptr(),
                &mut mode_count,
                modes.as_mut_ptr(),
                None,
            );
            if status == ERROR_INSUFFICIENT_BUFFER {
                continue;
            }
            status.ok()?;
            paths.truncate(count as usize);
            let monitors = Monitor::enumerate()?;
            let mut displays = Vec::new();
            for path in paths {
                let mut source = DISPLAYCONFIG_SOURCE_DEVICE_NAME {
                    header: header::<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(
                        DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,
                        path.sourceInfo.adapterId,
                        path.sourceInfo.id,
                    ),
                    ..Default::default()
                };
                if DisplayConfigGetDeviceInfo(&mut source.header) != 0 {
                    return Err("Display source metadata unavailable".into());
                }
                let name = text(&source.viewGdiDeviceName);
                let monitor = monitors
                    .iter()
                    .find(|m| m.device_name().ok().as_ref() == Some(&name))
                    .ok_or("Active display source has no upstream monitor")?;
                let mut target = DISPLAYCONFIG_TARGET_DEVICE_NAME {
                    header: header::<DISPLAYCONFIG_TARGET_DEVICE_NAME>(
                        DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
                        path.targetInfo.adapterId,
                        path.targetInfo.id,
                    ),
                    ..Default::default()
                };
                if DisplayConfigGetDeviceInfo(&mut target.header) != 0 {
                    return Err("Display target metadata unavailable".into());
                }
                let device_path = text(&target.monitorDevicePath).to_lowercase();
                if device_path.is_empty() {
                    return Err("Display has no device-interface identity".into());
                }
                let handle = HMONITOR(monitor.as_raw_hmonitor());
                let mut info = MONITORINFO {
                    cbSize: std::mem::size_of::<MONITORINFO>() as u32,
                    ..Default::default()
                };
                if !GetMonitorInfoW(handle, &mut info).as_bool() {
                    return Err(windows::core::Error::from_thread().into());
                }
                let mut color = DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO {
                    header: header::<DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO>(
                        DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO,
                        path.targetInfo.adapterId,
                        path.targetInfo.id,
                    ),
                    ..Default::default()
                };
                let color_state = if DisplayConfigGetDeviceInfo(&mut color.header) == 0 {
                    Some(color.Anonymous.value)
                } else {
                    None
                };
                let dpi = effective_dpi(info.rcMonitor.left + 1, info.rcMonitor.top + 1)?;
                displays.push(Display {
                    display_id: format!("monitor:{device_path}"),
                    device_name: name,
                    bounds: Rect {
                        x: info.rcMonitor.left,
                        y: info.rcMonitor.top,
                        width: (info.rcMonitor.right - info.rcMonitor.left) as u32,
                        height: (info.rcMonitor.bottom - info.rcMonitor.top) as u32,
                    },
                    rotation: path.targetInfo.rotation.0,
                    refresh_numerator: path.targetInfo.refreshRate.Numerator,
                    refresh_denominator: path.targetInfo.refreshRate.Denominator,
                    scale_percent: (dpi * 100 / 96) as i32,
                    primary: info.dwFlags & 1 != 0,
                    identity_quality: "device-interface-path".into(),
                    advanced_color_state: color_state,
                    dpi,
                    adapter_id: format!(
                        "{}:{}",
                        path.targetInfo.adapterId.HighPart, path.targetInfo.adapterId.LowPart
                    ),
                    target_id: path.targetInfo.id,
                });
            }
            displays.sort_by(|a, b| a.display_id.cmp(&b.display_id));
            if displays
                .windows(2)
                .any(|w| w[0].display_id == w[1].display_id)
            {
                return Err("Ambiguous display device paths".into());
            }
            let signature = serde_json::to_string(&displays)?;
            return Ok(Topology {
                schema: "aipet.topology/1",
                signature,
                displays,
            });
        }
    }
    Err("Topology changed during all three snapshots".into())
}
pub fn select(topology: &Topology, id: Option<&str>) -> Result<(Monitor, Display), Error> {
    let display = topology
        .displays
        .iter()
        .find(|d| id.map_or(d.primary, |id| d.display_id == id))
        .ok_or("Requested display unavailable")?
        .clone();
    if topology
        .displays
        .iter()
        .filter(|d| d.device_name == display.device_name)
        .count()
        != 1
    {
        return Err("Cloned source cannot identify independent monitor captures".into());
    }
    let monitor = Monitor::enumerate()?
        .into_iter()
        .find(|m| m.device_name().ok().as_ref() == Some(&display.device_name))
        .ok_or("Upstream monitor disappeared")?;
    Ok((monitor, display))
}
pub fn local_roi(roi: Rect, bounds: Rect) -> Result<Rect, Error> {
    let x = i64::from(roi.x) - i64::from(bounds.x);
    let y = i64::from(roi.y) - i64::from(bounds.y);
    if x < 0
        || y < 0
        || roi.width == 0
        || roi.height == 0
        || x + i64::from(roi.width) > i64::from(bounds.width)
        || y + i64::from(roi.height) > i64::from(bounds.height)
    {
        return Err("ROI outside selected display".into());
    }
    Ok(Rect {
        x: x.try_into()?,
        y: y.try_into()?,
        width: roi.width,
        height: roi.height,
    })
}
#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn negative_desktop_coordinates_map_to_monitor_texture() {
        let bounds = Rect {
            x: -1920,
            y: -300,
            width: 1920,
            height: 1080,
        };
        let roi = local_roi(
            Rect {
                x: -1900,
                y: -280,
                width: 32,
                height: 64,
            },
            bounds,
        )
        .unwrap();
        assert_eq!((roi.x, roi.y), (20, 20));
        assert!(
            local_roi(
                Rect {
                    x: -2000,
                    y: -280,
                    width: 32,
                    height: 64
                },
                bounds
            )
            .is_err()
        );
    }
}
