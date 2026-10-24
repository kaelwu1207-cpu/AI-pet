use crate::gpu_analysis::{Analysis, Rect};
use windows::Win32::Graphics::{
    Direct3D::D3D_DRIVER_TYPE_HARDWARE, Direct3D11::*, Dxgi::Common::*,
};

#[test]
fn frozen_gpu_history_preserves_lines_and_rejects_local_changes() {
    unsafe {
        let mut device = None;
        let mut context = None;
        D3D11CreateDevice(
            None,
            D3D_DRIVER_TYPE_HARDWARE,
            Default::default(),
            D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            None,
            D3D11_SDK_VERSION,
            Some(&mut device),
            None,
            Some(&mut context),
        )
        .unwrap();
        let device = device.unwrap();
        let context = context.unwrap();
        let mut texture = None;
        device
            .CreateTexture2D(
                &D3D11_TEXTURE2D_DESC {
                    Width: 64,
                    Height: 64,
                    MipLevels: 1,
                    ArraySize: 1,
                    Format: DXGI_FORMAT_R8G8B8A8_UNORM,
                    SampleDesc: DXGI_SAMPLE_DESC {
                        Count: 1,
                        Quality: 0,
                    },
                    Usage: D3D11_USAGE_DEFAULT,
                    ..Default::default()
                },
                None,
                Some(&mut texture),
            )
            .unwrap();
        let texture = texture.unwrap();
        for tile_size in [16, 64] {
            let analysis = Analysis::new_texture_tiled(
                &device,
                &texture,
                Rect {
                    x: 0,
                    y: 0,
                    width: 64,
                    height: 64,
                },
                tile_size,
            )
            .unwrap();
            let mut pixels = vec![0u8; 64 * 64 * 4];
            for y in 0..64 {
                let i = (y * 64 + 16) * 4;
                pixels[i..i + 3].fill(255);
            }
            let sample = |pixels: &[u8], delta, reset, masks: &[Rect]| {
                context.UpdateSubresource(&texture, 0, None, pixels.as_ptr().cast(), 64 * 4, 0);
                analysis
                    .observe_texture(&context, &texture, delta, reset, masks)
                    .unwrap()
            };
            sample(&pixels, 0, true, &[]);
            let early = sample(&pixels, 499, false, &[]);
            assert_eq!(
                early
                    .iter()
                    .map(|t| t["StableEdges"].as_u64().unwrap())
                    .sum::<u64>(),
                0
            );
            let stable = sample(&pixels, 1, false, &[]);
            assert_eq!(
                stable
                    .iter()
                    .map(|t| t["StableEdges"].as_u64().unwrap())
                    .sum::<u64>(),
                64
            );
            let caret = (10 * 64 + 18) * 4;
            pixels[caret..caret + 3].fill(255);
            let blink = sample(&pixels, 120, false, &[]);
            assert_eq!(
                blink
                    .iter()
                    .map(|t| t["StableEdges"].as_u64().unwrap())
                    .sum::<u64>(),
                64
            );
            assert_eq!(
                blink
                    .iter()
                    .map(|t| t["Changed"].as_u64().unwrap())
                    .sum::<u64>(),
                1
            );
            let mask = Rect {
                x: 0,
                y: 0,
                width: 32,
                height: 64,
            };
            let hidden = sample(&pixels, 120, false, &[mask]);
            assert_eq!(
                hidden
                    .iter()
                    .map(|t| t["Masked"].as_u64().unwrap())
                    .sum::<u64>(),
                2048
            );
            let reveal = sample(&pixels, 120, false, &[]);
            assert_eq!(
                reveal
                    .iter()
                    .map(|t| t["StableEdges"].as_u64().unwrap())
                    .sum::<u64>(),
                0
            );
            // The inclusive 7/255 tolerance and 8/255 reset are frozen independently of edge shape.
            pixels.fill(0);
            sample(&pixels, 0, true, &[]);
            pixels[0] = 7;
            let tolerated = sample(&pixels, 500, false, &[]);
            assert_eq!(tolerated[0]["Changed"], 0);
            pixels[0] = 15;
            let changed = sample(&pixels, 120, false, &[]);
            assert_eq!(changed[0]["Changed"], 1);
            let reset = sample(&pixels, 0, true, &[]);
            assert!(reset.iter().all(|t| t["MaxAgeMs"] == 0));
            for (level, expected_edges) in [(32, 0), (50, 0), (52, 64)] {
                pixels.fill(0);
                for y in 0..64 {
                    let i = (y * 64 + 16) * 4;
                    pixels[i..i + 3].fill(level);
                }
                sample(&pixels, 0, true, &[]);
                let counts = sample(&pixels, 500, false, &[]);
                assert_eq!(
                    counts
                        .iter()
                        .map(|t| t["StableEdges"].as_u64().unwrap())
                        .sum::<u64>(),
                    expected_edges
                );
            }
            let capped = sample(&pixels, 4_000_000, false, &[]);
            assert!(capped.iter().all(|t| t["MaxAgeMs"] == 3_600_000));
            let partial = Analysis::new_texture(
                &device,
                &texture,
                Rect {
                    x: 1,
                    y: 1,
                    width: 17,
                    height: 17,
                },
            )
            .unwrap();
            let counts = partial
                .observe_texture(&context, &texture, 0, true, &[])
                .unwrap();
            assert_eq!(
                counts
                    .iter()
                    .map(|t| t["Visible"].as_u64().unwrap())
                    .sum::<u64>(),
                289
            );
        }
    }
}
