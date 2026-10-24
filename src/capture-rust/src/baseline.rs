use serde::Deserialize;
#[derive(Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct Baseline {
    pub schema: String,
    pub pixel_delta: u32,
    pub stable_ms: u32,
    pub edge_brightness: f32,
    pub edge_contrast: f32,
    pub age_cap_ms: u32,
    pub capture_interval_ms: u64,
    pub fresh_ms: u64,
    pub gap_ms: u32,
    pub maximum_pixels: u64,
    pub owned_gpu_budget_bytes: u64,
}
pub fn frozen() -> Baseline {
    serde_json::from_str(include_str!("../../../contracts/static-v1.json"))
        .expect("compiled static baseline")
}
