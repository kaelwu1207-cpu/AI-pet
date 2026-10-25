use aipet_gpu_probe::gpu_analysis::{Analysis, Rect};
use serde::Deserialize;
use std::{
    io::{self, BufRead, Write},
    sync::{
        Arc, Mutex,
        atomic::{AtomicBool, AtomicU64, Ordering},
    },
    thread,
    time::{Duration, Instant},
};
use windows_capture::{
    capture::{Context, GraphicsCaptureApiHandler},
    frame::Frame,
    graphics_capture_api::InternalCaptureControl,
    settings::*,
};
type Error = Box<dyn std::error::Error + Send + Sync>;
#[derive(Clone, Default, Deserialize)]
#[serde(rename_all = "PascalCase")]
struct Masks {
    revision: u32,
    masks: Vec<Rect>,
}
#[derive(Deserialize)]
#[serde(rename_all = "PascalCase")]
struct Config {
    roi: Rect,
    display_id: Option<String>,
    expected_signature: Option<String>,
    #[serde(default)]
    topology_epoch: i64,
    #[serde(default = "default_tile")]
    tile_size: u32,
    #[serde(default = "default_duration")]
    duration_ms: u64,
    #[serde(default)]
    resident: bool,
}
fn default_tile() -> u32 {
    16
}
fn default_duration() -> u64 {
    15000
}
#[derive(Clone)]
struct Flags {
    roi: Rect,
    local_roi: Rect,
    display: aipet_gpu_probe::topology::Display,
    topology_epoch: i64,
    tile_size: u32,
    masks: Arc<Mutex<Masks>>,
    frames: Arc<AtomicU64>,
}
struct Capture {
    flags: Flags,
    analysis: Option<Analysis>,
    last: i64,
}
impl GraphicsCaptureApiHandler for Capture {
    type Flags = Flags;
    type Error = Error;
    fn new(context: Context<Flags>) -> Result<Self, Error> {
        Ok(Self {
            flags: context.flags,
            analysis: None,
            last: 0,
        })
    }
    fn on_frame_arrived(
        &mut self,
        frame: &mut Frame,
        _: InternalCaptureControl,
    ) -> Result<(), Error> {
        let stamp = frame.timestamp()?.Duration;
        if stamp <= self.last {
            return Err("Nonmonotonic source frame".into());
        }
        let delta = if self.last == 0 {
            0
        } else {
            ((stamp - self.last) / 10000) as u32
        };
        let reset = self.last == 0 || delta > aipet_gpu_probe::baseline::frozen().gap_ms;
        self.last = stamp;
        if self.analysis.is_none() {
            self.analysis = Some(Analysis::new_tiled(
                frame,
                self.flags.local_roi,
                self.flags.tile_size,
            )?);
        }
        let masks = self
            .flags
            .masks
            .lock()
            .map_err(|_| "mask lock poisoned")?
            .clone();
        let started = Instant::now();
        let local_masks: Vec<Rect> = masks
            .masks
            .iter()
            .map(|r| Rect {
                x: r.x - self.flags.display.bounds.x,
                y: r.y - self.flags.display.bounds.y,
                ..*r
            })
            .collect();
        let mut tiles = self.analysis.as_ref().unwrap().observe(
            frame,
            if reset { 0 } else { delta },
            reset,
            &local_masks,
        )?;
        for tile in &mut tiles {
            tile["X"] = serde_json::json!(
                tile["X"].as_i64().ok_or("Bad tile X")? + i64::from(self.flags.display.bounds.x)
            );
            tile["Y"] = serde_json::json!(
                tile["Y"].as_i64().ok_or("Bad tile Y")? + i64::from(self.flags.display.bounds.y)
            );
        }
        let wall_ms = started.elapsed().as_secs_f64() * 1000.0;
        let metrics = self.analysis.as_ref().unwrap().metrics(frame)?;
        let sequence = self.flags.frames.fetch_add(1, Ordering::SeqCst) + 1;
        println!(
            "{}",
            serde_json::json!({"Sequence":sequence,"Timestamp100ns":stamp,"MaskRevision":masks.revision,
            "Roi":self.flags.roi,"Tiles":tiles,"Healthy":true,"GapReset":reset,"PixelReadback":false,
            "StatisticReadbackBytes":tiles.len()*32,"TileSize":self.flags.tile_size,"AnalysisWallMs":wall_ms,"Performance":metrics,
            "DisplayId":self.flags.display.display_id,"TopologyEpoch":self.flags.topology_epoch})
        );
        io::stdout().flush()?;
        Ok(())
    }
}
fn run() -> Result<i32, Error> {
    unsafe {
        windows::Win32::UI::HiDpi::SetProcessDpiAwarenessContext(
            windows::Win32::UI::HiDpi::DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2,
        )?;
    }
    if std::env::args().nth(1).as_deref() == Some("--topology") {
        println!(
            "{}",
            serde_json::to_string(&aipet_gpu_probe::topology::snapshot()?)?
        );
        return Ok(0);
    }
    let mut first = String::new();
    io::stdin().read_line(&mut first)?;
    if first.len() > 65536 {
        return Err("Configuration budget exceeded".into());
    }
    let config: Config = serde_json::from_str(&first)?;
    if !(1000..=30000).contains(&config.duration_ms) {
        return Err("Duration budget 1..30 seconds".into());
    }
    let topology = aipet_gpu_probe::topology::snapshot()?;
    if config
        .expected_signature
        .as_ref()
        .is_some_and(|s| s != &topology.signature)
    {
        return Err("Topology changed before capture startup".into());
    }
    let (monitor, display) =
        aipet_gpu_probe::topology::select(&topology, config.display_id.as_deref())?;
    let local_roi = aipet_gpu_probe::topology::local_roi(config.roi, display.bounds)?;
    let stop = Arc::new(AtomicBool::new(false));
    let restart = Arc::new(AtomicBool::new(false));
    let command_restart = Arc::clone(&restart);
    let masks = Arc::new(Mutex::new(Masks::default()));
    let frames = Arc::new(AtomicU64::new(0));
    let command_stop = Arc::clone(&stop);
    let command_masks = Arc::clone(&masks);
    let lease_origin = Instant::now();
    let lease = Arc::new(AtomicU64::new(0));
    let command_lease = Arc::clone(&lease);
    thread::spawn(move || {
        for line in io::stdin().lock().lines() {
            let Ok(line) = line else { break };
            if line == "heartbeat" {
                command_lease.store(lease_origin.elapsed().as_millis() as u64, Ordering::SeqCst);
                continue;
            }
            if line == "restart" {
                command_restart.store(true, Ordering::SeqCst);
                break;
            }
            if line == "stop" || line.len() > 65536 {
                break;
            }
            match serde_json::from_str::<Masks>(&line) {
                Ok(next) if next.masks.len() <= 16 => {
                    let mut current = command_masks.lock().unwrap();
                    if next.revision > current.revision {
                        *current = next;
                    }
                }
                _ => break,
            }
        }
        command_stop.store(true, Ordering::SeqCst);
    });
    let settings = Settings::new(
        monitor,
        CursorCaptureSettings::WithoutCursor,
        DrawBorderSettings::Default,
        SecondaryWindowSettings::Default,
        MinimumUpdateIntervalSettings::Custom(Duration::from_millis(
            aipet_gpu_probe::baseline::frozen().capture_interval_ms,
        )),
        DirtyRegionSettings::Default,
        ColorFormat::Rgba8,
        Flags {
            roi: config.roi,
            local_roi,
            display,
            topology_epoch: config.topology_epoch,
            tile_size: config.tile_size,
            masks,
            frames: Arc::clone(&frames),
        },
    );
    let control = Capture::start_free_threaded(settings)?;
    let start = Instant::now();
    let mut next_topology = Instant::now() + Duration::from_millis(500);
    let mut reason = None;
    while !stop.load(Ordering::SeqCst)
        && !control.is_finished()
        && (config.resident || start.elapsed() < Duration::from_millis(config.duration_ms))
    {
        if config.resident
            && (lease_origin.elapsed().as_millis() as u64)
                .saturating_sub(lease.load(Ordering::SeqCst)) > 2000
        {
            reason = Some("host-lease-expired");
            break;
        }
        if Instant::now() >= next_topology {
            match aipet_gpu_probe::topology::snapshot() {
                Ok(next) if next.signature == topology.signature => {}
                _ => {
                    reason = Some("topology-changed-or-unavailable");
                    break;
                }
            }
            next_topology = Instant::now() + Duration::from_millis(500);
        }
        thread::sleep(Duration::from_millis(20));
    }
    control.stop()?;
    let code = if reason.is_some() {
        3
    } else if restart.load(Ordering::SeqCst) {
        reason = Some("requested-restart");
        4
    } else {
        0
    };
    println!(
        "{}",
        serde_json::json!({"Kind":"end","Frames":frames.load(Ordering::SeqCst),"PixelsPersisted":false,"ExitCode":code,"Reason":reason})
    );
    Ok(code)
}
fn main() {
    match run() {
        Ok(code) => std::process::exit(code),
        Err(error) => {
            eprintln!("{error}");
            std::process::exit(1);
        }
    }
}
