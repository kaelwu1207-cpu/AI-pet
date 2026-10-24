//! Consume upstream GPU textures within the callback; no pixel readback or file encoding.
use serde::Serialize;
use std::{
    sync::{Arc, Mutex},
    thread,
    time::{Duration, Instant},
};
use windows::Win32::Graphics::Direct3D11::*;
use windows_capture::{
    capture::{Context, GraphicsCaptureApiHandler},
    frame::Frame,
    graphics_capture_api::InternalCaptureControl,
    monitor::Monitor,
    settings::*,
};

type Error = Box<dyn std::error::Error + Send + Sync>;
#[derive(Default, Serialize)]
struct Stats {
    monitor: usize,
    frames: u64,
    gpu_copies_completed: u64,
    width: u32,
    height: u32,
    format: i32,
    last_timestamp: i64,
}
struct Consumer {
    stats: Arc<Mutex<Stats>>,
    texture: Option<ID3D11Texture2D>,
}
impl GraphicsCaptureApiHandler for Consumer {
    type Flags = Arc<Mutex<Stats>>;
    type Error = Error;
    fn new(ctx: Context<Self::Flags>) -> Result<Self, Error> {
        Ok(Self {
            stats: ctx.flags,
            texture: None,
        })
    }
    fn on_frame_arrived(
        &mut self,
        frame: &mut Frame,
        _: InternalCaptureControl,
    ) -> Result<(), Error> {
        let source = frame.as_raw_texture();
        let device = frame.device();
        let context = frame.device_context();
        let mut desc = D3D11_TEXTURE2D_DESC::default();
        // SAFETY: upstream owns a valid texture/device/context for this callback. Copies and
        // the completion query finish before returning; no borrowed texture is retained.
        unsafe {
            source.GetDesc(&mut desc);
            if let Some(target) = &self.texture {
                let mut previous = D3D11_TEXTURE2D_DESC::default();
                target.GetDesc(&mut previous);
                if previous.Width != desc.Width
                    || previous.Height != desc.Height
                    || previous.Format != desc.Format
                {
                    self.texture = None;
                }
            }
            if self.texture.is_none() {
                let mut owned = desc;
                owned.Usage = D3D11_USAGE_DEFAULT;
                owned.CPUAccessFlags = 0;
                owned.BindFlags = 0;
                owned.MiscFlags = 0;
                device.CreateTexture2D(&owned, None, Some(&mut self.texture))?;
            }
            context.CopyResource(self.texture.as_ref().unwrap(), source);
            let mut query = None;
            device.CreateQuery(
                &D3D11_QUERY_DESC {
                    Query: D3D11_QUERY_EVENT,
                    MiscFlags: 0,
                },
                Some(&mut query),
            )?;
            let query = query.unwrap();
            context.End(&query);
            context.Flush();
            let deadline = Instant::now() + Duration::from_secs(2);
            loop {
                let mut ready = 0u32;
                context.GetData(&query, Some((&mut ready as *mut u32).cast()), 4, 0)?;
                if ready != 0 {
                    break;
                }
                if Instant::now() >= deadline {
                    return Err("GPU copy completion timeout".into());
                }
                thread::sleep(Duration::from_millis(1));
            }
        }
        let timestamp = frame.timestamp()?.Duration;
        let mut stats = self.stats.lock().map_err(|_| "stats lock poisoned")?;
        if stats.frames > 0 && timestamp <= stats.last_timestamp {
            return Err("nonmonotonic frame".into());
        }
        stats.frames += 1;
        stats.gpu_copies_completed += 1;
        stats.width = desc.Width;
        stats.height = desc.Height;
        stats.format = desc.Format.0;
        stats.last_timestamp = timestamp;
        Ok(())
    }
}
fn run() -> Result<(), Error> {
    let minimum: usize = std::env::args()
        .nth(1)
        .unwrap_or_else(|| "1".into())
        .parse()?;
    if !(1..=16).contains(&minimum) {
        return Err("minimum displays must be 1..16".into());
    }
    let monitors = Monitor::enumerate()?;
    let mut controls = Vec::new();
    let mut reports = Vec::new();
    let mut errors = Vec::new();
    for (index, monitor) in monitors.into_iter().enumerate() {
        let stats = Arc::new(Mutex::new(Stats {
            monitor: index + 1,
            ..Default::default()
        }));
        let settings = Settings::new(
            monitor,
            CursorCaptureSettings::WithoutCursor,
            DrawBorderSettings::Default,
            SecondaryWindowSettings::Default,
            MinimumUpdateIntervalSettings::Custom(Duration::from_millis(100)),
            DirtyRegionSettings::Default,
            ColorFormat::Rgba16F,
            Arc::clone(&stats),
        );
        match Consumer::start_free_threaded(settings) {
            Ok(control) => controls.push(control),
            Err(error) => errors.push(format!("monitor {} start: {error}", index + 1)),
        }
        reports.push(stats);
    }
    thread::sleep(Duration::from_secs(4));
    for control in controls {
        if let Err(error) = control.stop() {
            errors.push(format!("stop: {error}"));
        }
    }
    let reports: Vec<_> = reports.iter().map(|s| s.lock().unwrap()).collect();
    let passed = reports.len() >= minimum
        && errors.is_empty()
        && reports
            .iter()
            .all(|s| s.frames >= 3 && s.frames == s.gpu_copies_completed);
    println!(
        "{}",
        serde_json::to_string(&serde_json::json!({
            "schema":"aipet.gpu-probe/0.1", "library":"windows-capture 2.0.1",
            "minimum_displays":minimum, "display_count":reports.len(),
            "displays":reports.iter().map(|s| &**s).collect::<Vec<_>>(), "errors":errors,
            "consumer":"D3D11 CopyResource + completed EVENT query", "pixel_readback":false,
            "pixels_persisted":false, "hdr_display_verified":false, "passed":passed
        }))?
    );
    if !passed {
        std::process::exit(2);
    }
    Ok(())
}
fn main() {
    if let Err(error) = run() {
        eprintln!("{error}");
        std::process::exit(1);
    }
}
