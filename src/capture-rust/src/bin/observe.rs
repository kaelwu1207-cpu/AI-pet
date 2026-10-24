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
    monitor::Monitor,
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
}
#[derive(Clone)]
struct Flags {
    roi: Rect,
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
            self.analysis = Some(Analysis::new(frame, self.flags.roi)?);
        }
        let masks = self
            .flags
            .masks
            .lock()
            .map_err(|_| "mask lock poisoned")?
            .clone();
        let tiles = self.analysis.as_ref().unwrap().observe(
            frame,
            if reset { 0 } else { delta },
            reset,
            &masks.masks,
        )?;
        let sequence = self.flags.frames.fetch_add(1, Ordering::SeqCst) + 1;
        println!(
            "{}",
            serde_json::json!({"Sequence":sequence,"Timestamp100ns":stamp,"MaskRevision":masks.revision,
            "Roi":self.flags.roi,"Tiles":tiles,"Healthy":true,"GapReset":reset,"PixelReadback":false,
            "StatisticReadbackBytes":tiles.len()*32})
        );
        io::stdout().flush()?;
        Ok(())
    }
}
fn run() -> Result<(), Error> {
    let mut first = String::new();
    io::stdin().read_line(&mut first)?;
    if first.len() > 65536 {
        return Err("Configuration budget exceeded".into());
    }
    let config: Config = serde_json::from_str(&first)?;
    let stop = Arc::new(AtomicBool::new(false));
    let masks = Arc::new(Mutex::new(Masks::default()));
    let frames = Arc::new(AtomicU64::new(0));
    let command_stop = Arc::clone(&stop);
    let command_masks = Arc::clone(&masks);
    thread::spawn(move || {
        for line in io::stdin().lock().lines() {
            let Ok(line) = line else { break };
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
        Monitor::primary()?,
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
            masks,
            frames: Arc::clone(&frames),
        },
    );
    let control = Capture::start_free_threaded(settings)?;
    let start = Instant::now();
    while !stop.load(Ordering::SeqCst)
        && !control.is_finished()
        && start.elapsed() < Duration::from_secs(15)
    {
        thread::sleep(Duration::from_millis(20));
    }
    control.stop()?;
    println!(
        "{}",
        serde_json::json!({"Kind":"end","Frames":frames.load(Ordering::SeqCst),"PixelsPersisted":false,"ExitCode":0})
    );
    Ok(())
}
fn main() {
    if let Err(error) = run() {
        eprintln!("{error}");
        std::process::exit(1);
    }
}
