//! Bounded GPU statistics over upstream WGC textures. Readback contains counts, never pixels.
use serde::{Deserialize, Serialize};
use windows::{
    Win32::Graphics::{
        Direct3D::{Fxc::D3DCompile, ID3DBlob},
        Direct3D11::*,
        Dxgi::{
            Common::DXGI_FORMAT_UNKNOWN, DXGI_MEMORY_SEGMENT_GROUP_LOCAL,
            DXGI_QUERY_VIDEO_MEMORY_INFO, IDXGIAdapter3, IDXGIDevice,
        },
    },
    core::{Interface, s},
};
use windows_capture::frame::Frame;
type Error = Box<dyn std::error::Error + Send + Sync>;

#[derive(Clone, Copy, Debug, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct Rect {
    pub x: i32,
    pub y: i32,
    pub width: u32,
    pub height: u32,
}
#[repr(C)]
#[derive(Clone, Copy, Default)]
struct Counts {
    visible: u32,
    stable: u32,
    edges: u32,
    max_age: u32,
    edge_age: u32,
    changed: u32,
    luma: u32,
    masked: u32,
}
#[repr(C)]
struct Params {
    header: [u32; 8],
    masks: [[i32; 4]; 16],
}
pub struct Analysis {
    roi: Rect,
    texture: ID3D11Texture2D,
    srv: ID3D11ShaderResourceView,
    shader: ID3D11ComputeShader,
    history: ID3D11UnorderedAccessView,
    output: ID3D11Buffer,
    output_view: ID3D11UnorderedAccessView,
    staging: ID3D11Buffer,
    params: ID3D11Buffer,
    columns: u32,
    rows: u32,
    source_shape: (u32, u32, i32),
    timestamps: [ID3D11Query; 3],
    owned_bytes: u64,
}
unsafe fn buffer(
    device: &ID3D11Device,
    count: u32,
    stride: u32,
    staging: bool,
) -> Result<ID3D11Buffer, Error> {
    let desc = D3D11_BUFFER_DESC {
        ByteWidth: count * stride,
        Usage: if staging {
            D3D11_USAGE_STAGING
        } else {
            D3D11_USAGE_DEFAULT
        },
        BindFlags: if staging {
            0
        } else {
            D3D11_BIND_UNORDERED_ACCESS.0 as u32
        },
        CPUAccessFlags: if staging {
            D3D11_CPU_ACCESS_READ.0 as u32
        } else {
            0
        },
        MiscFlags: if staging {
            0
        } else {
            D3D11_RESOURCE_MISC_BUFFER_STRUCTURED.0 as u32
        },
        StructureByteStride: if staging { 0 } else { stride },
    };
    let mut result = None;
    unsafe {
        device.CreateBuffer(&desc, None, Some(&mut result))?;
    }
    Ok(result.ok_or("buffer creation returned null")?)
}
unsafe fn uav(
    device: &ID3D11Device,
    resource: &ID3D11Buffer,
    count: u32,
) -> Result<ID3D11UnorderedAccessView, Error> {
    let desc = D3D11_UNORDERED_ACCESS_VIEW_DESC {
        Format: DXGI_FORMAT_UNKNOWN,
        ViewDimension: D3D11_UAV_DIMENSION_BUFFER,
        Anonymous: D3D11_UNORDERED_ACCESS_VIEW_DESC_0 {
            Buffer: D3D11_BUFFER_UAV {
                FirstElement: 0,
                NumElements: count,
                Flags: 0,
            },
        },
    };
    let mut view = None;
    unsafe {
        device.CreateUnorderedAccessView(resource, Some(&desc), Some(&mut view))?;
    }
    Ok(view.ok_or("UAV creation returned null")?)
}
impl Analysis {
    pub fn new(frame: &Frame, roi: Rect) -> Result<Self, Error> {
        Self::new_texture(frame.device(), frame.as_raw_texture(), roi)
    }
    pub fn new_texture(
        device: &ID3D11Device,
        source_texture: &ID3D11Texture2D,
        roi: Rect,
    ) -> Result<Self, Error> {
        let baseline = crate::baseline::frozen();
        if roi.x < 0
            || roi.y < 0
            || roi.width == 0
            || roi.height == 0
            || roi.width as u64 * roi.height as u64 > baseline.maximum_pixels
        {
            return Err("ROI exceeds the frozen pixel budget".into());
        }
        // SAFETY: all resources are created on the upstream frame's device; borrowed surfaces
        // are used only inside the capture callback, output mappings only copy POD counts.
        unsafe {
            let mut desc = D3D11_TEXTURE2D_DESC::default();
            source_texture.GetDesc(&mut desc);
            if roi.x as u64 + roi.width as u64 > desc.Width as u64
                || roi.y as u64 + roi.height as u64 > desc.Height as u64
            {
                return Err("ROI outside captured display".into());
            }
            let source_shape = (desc.Width, desc.Height, desc.Format.0);
            desc.Width = roi.width;
            desc.Height = roi.height;
            desc.Usage = D3D11_USAGE_DEFAULT;
            desc.BindFlags = D3D11_BIND_SHADER_RESOURCE.0 as u32;
            desc.CPUAccessFlags = 0;
            desc.MiscFlags = 0;
            let mut texture = None;
            device.CreateTexture2D(&desc, None, Some(&mut texture))?;
            let texture = texture.ok_or("texture missing")?;
            let mut srv = None;
            device.CreateShaderResourceView(&texture, None, Some(&mut srv))?;
            let source = format!(
                "#define PIXEL_DELTA {}\n#define EDGE_BRIGHTNESS {}\n#define EDGE_CONTRAST {}\n#define AGE_CAP {}\n{}",
                baseline.pixel_delta,
                baseline.edge_brightness,
                baseline.edge_contrast,
                baseline.age_cap_ms,
                include_str!("static_tiles.hlsl")
            );
            let mut code = None;
            let mut errors: Option<ID3DBlob> = None;
            if let Err(error) = D3DCompile(
                source.as_ptr().cast(),
                source.len(),
                s!("static_tiles.hlsl"),
                None,
                None,
                s!("main"),
                s!("cs_5_0"),
                0,
                0,
                &mut code,
                Some(&mut errors),
            ) {
                if let Some(log) = errors {
                    return Err(String::from_utf8_lossy(std::slice::from_raw_parts(
                        log.GetBufferPointer().cast(),
                        log.GetBufferSize(),
                    ))
                    .into_owned()
                    .into());
                }
                return Err(error.into());
            }
            let code = code.ok_or("compiled shader missing")?;
            let mut shader = None;
            device.CreateComputeShader(
                std::slice::from_raw_parts(code.GetBufferPointer().cast(), code.GetBufferSize()),
                None,
                Some(&mut shader),
            )?;
            let columns = roi.width.div_ceil(16);
            let rows = roi.height.div_ceil(16);
            let tiles = columns * rows;
            let owned_bytes = roi.width as u64 * roi.height as u64 * 12
                + tiles as u64 * 64
                + std::mem::size_of::<Params>() as u64;
            if owned_bytes > baseline.owned_gpu_budget_bytes {
                return Err("Owned GPU allocation exceeds frozen budget".into());
            }
            let state = buffer(device, roi.width * roi.height, 8, false)?;
            let output = buffer(device, tiles, 32, false)?;
            let staging = buffer(device, tiles, 32, true)?;
            let history = uav(device, &state, roi.width * roi.height)?;
            let output_view = uav(device, &output, tiles)?;
            let mut params = None;
            device.CreateBuffer(
                &D3D11_BUFFER_DESC {
                    ByteWidth: std::mem::size_of::<Params>() as u32,
                    Usage: D3D11_USAGE_DEFAULT,
                    BindFlags: D3D11_BIND_CONSTANT_BUFFER.0 as u32,
                    ..Default::default()
                },
                None,
                Some(&mut params),
            )?;
            let query = |kind| -> Result<ID3D11Query, Error> {
                let mut q = None;
                device.CreateQuery(
                    &D3D11_QUERY_DESC {
                        Query: kind,
                        MiscFlags: 0,
                    },
                    Some(&mut q),
                )?;
                Ok(q.ok_or("Query missing")?)
            };
            Ok(Self {
                roi,
                texture,
                srv: srv.ok_or("SRV missing")?,
                shader: shader.ok_or("shader missing")?,
                history,
                output,
                output_view,
                staging,
                params: params.ok_or("parameters missing")?,
                columns,
                rows,
                source_shape,
                timestamps: [
                    query(D3D11_QUERY_TIMESTAMP_DISJOINT)?,
                    query(D3D11_QUERY_TIMESTAMP)?,
                    query(D3D11_QUERY_TIMESTAMP)?,
                ],
                owned_bytes,
            })
        }
    }
    pub fn observe(
        &self,
        frame: &Frame,
        delta_ms: u32,
        reset: bool,
        masks: &[Rect],
    ) -> Result<Vec<serde_json::Value>, Error> {
        self.observe_texture(
            frame.device_context(),
            frame.as_raw_texture(),
            delta_ms,
            reset,
            masks,
        )
    }
    pub fn observe_texture(
        &self,
        context: &ID3D11DeviceContext,
        source_texture: &ID3D11Texture2D,
        delta_ms: u32,
        reset: bool,
        masks: &[Rect],
    ) -> Result<Vec<serde_json::Value>, Error> {
        if masks.len() > 16 {
            return Err("self-mask budget exceeded".into());
        }
        let mut params = Params {
            header: [
                self.roi.width,
                self.roi.height,
                self.columns,
                delta_ms,
                u32::from(reset),
                masks.len() as u32,
                crate::baseline::frozen().stable_ms,
                0,
            ],
            masks: [[0; 4]; 16],
        };
        for (target, mask) in params.masks.iter_mut().zip(masks) {
            *target = [
                mask.x - self.roi.x,
                mask.y - self.roi.y,
                mask.width as i32,
                mask.height as i32,
            ];
        }
        unsafe {
            let mut shape = D3D11_TEXTURE2D_DESC::default();
            source_texture.GetDesc(&mut shape);
            if (shape.Width, shape.Height, shape.Format.0) != self.source_shape {
                return Err(
                    "Capture geometry or format changed; restart with a new topology epoch".into(),
                );
            }
            context.Begin(&self.timestamps[0]);
            context.End(&self.timestamps[1]);
            let area = D3D11_BOX {
                left: self.roi.x as u32,
                top: self.roi.y as u32,
                front: 0,
                right: self.roi.x as u32 + self.roi.width,
                bottom: self.roi.y as u32 + self.roi.height,
                back: 1,
            };
            context.CopySubresourceRegion(
                &self.texture,
                0,
                0,
                0,
                0,
                source_texture,
                0,
                Some(&area),
            );
            context.UpdateSubresource(
                &self.params,
                0,
                None,
                (&params as *const Params).cast(),
                0,
                0,
            );
            context.CSSetShader(&self.shader, None);
            context.CSSetShaderResources(0, Some(&[Some(self.srv.clone())]));
            context.CSSetConstantBuffers(0, Some(&[Some(self.params.clone())]));
            context.CSSetUnorderedAccessViews(
                0,
                2,
                Some([Some(self.history.clone()), Some(self.output_view.clone())].as_ptr()),
                None,
            );
            context.Dispatch(self.columns, self.rows, 1);
            context.CSSetUnorderedAccessViews(0, 2, Some([None, None].as_ptr()), None);
            context.CSSetShaderResources(0, Some(&[None]));
            context.End(&self.timestamps[2]);
            context.End(&self.timestamps[0]);
            context.CopyResource(&self.staging, &self.output);
            let mut mapped = D3D11_MAPPED_SUBRESOURCE::default();
            context.Map(&self.staging, 0, D3D11_MAP_READ, 0, Some(&mut mapped))?;
            let counts = std::slice::from_raw_parts(
                mapped.pData.cast::<Counts>(),
                (self.columns * self.rows) as usize,
            )
            .to_vec();
            context.Unmap(&self.staging, 0);
            Ok(counts.iter().enumerate().map(|(index,c)| {
                let x=index as u32%self.columns*16;let y=index as u32/self.columns*16;
                serde_json::json!({"X":self.roi.x+x as i32,"Y":self.roi.y+y as i32,"Width":16.min(self.roi.width-x),"Height":16.min(self.roi.height-y),
                    "Visible":c.visible,"StaticPixels":c.stable,"StableEdges":c.edges,"MaxAgeMs":c.max_age,"EdgeAgeMs":c.edge_age,
                    "Changed":c.changed,"LumaSum":c.luma,"Masked":c.masked})
            }).collect())
        }
    }
    pub fn metrics(&self, frame: &Frame) -> Result<serde_json::Value, Error> {
        unsafe {
            let context = frame.device_context();
            let mut disjoint = D3D11_QUERY_DATA_TIMESTAMP_DISJOINT::default();
            let mut begin = 0u64;
            let mut end = 0u64;
            context.GetData(
                &self.timestamps[0],
                Some((&mut disjoint as *mut D3D11_QUERY_DATA_TIMESTAMP_DISJOINT).cast()),
                std::mem::size_of_val(&disjoint) as u32,
                0,
            )?;
            context.GetData(
                &self.timestamps[1],
                Some((&mut begin as *mut u64).cast()),
                8,
                0,
            )?;
            context.GetData(
                &self.timestamps[2],
                Some((&mut end as *mut u64).cast()),
                8,
                0,
            )?;
            let gpu_ms = if !disjoint.Disjoint.as_bool() && disjoint.Frequency > 0 && end >= begin {
                Some((end - begin) as f64 * 1000.0 / disjoint.Frequency as f64)
            } else {
                None
            };
            let memory = (|| -> windows::core::Result<u64> {
                let dxgi: IDXGIDevice = frame.device().cast()?;
                let adapter: IDXGIAdapter3 = dxgi.GetAdapter()?.cast()?;
                let mut info = DXGI_QUERY_VIDEO_MEMORY_INFO::default();
                adapter.QueryVideoMemoryInfo(0, DXGI_MEMORY_SEGMENT_GROUP_LOCAL, &mut info)?;
                Ok(info.CurrentUsage)
            })()
            .ok();
            Ok(
                serde_json::json!({"GpuMs":gpu_ms,"GpuLocalMemoryBytes":memory,"OwnedGpuBytes":self.owned_bytes}),
            )
        }
    }
}
