#include "wgc_capture.hpp"
#include "frame_tracker.hpp"
#include <windows.graphics.capture.interop.h>
#include <windows.graphics.directx.direct3d11.interop.h>
#include <shellscalingapi.h>
#include <dxgi1_2.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Graphics.Capture.h>
#include <winrt/Windows.Graphics.DirectX.Direct3D11.h>
#include <algorithm>
#include <atomic>
#include <stdexcept>

using namespace winrt;
using namespace winrt::Windows::Graphics::Capture;
using namespace winrt::Windows::Graphics::DirectX;
using namespace winrt::Windows::Graphics::DirectX::Direct3D11;

namespace aipet {
namespace {
std::wstring target_path(const std::wstring& gdi) {
    UINT32 paths = 0, modes = 0;
    if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, &paths, &modes) != ERROR_SUCCESS) return {};
    std::vector<DISPLAYCONFIG_PATH_INFO> path(paths);
    std::vector<DISPLAYCONFIG_MODE_INFO> mode(modes);
    if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, &paths, path.data(), &modes, mode.data(), nullptr) != ERROR_SUCCESS) return {};
    for (UINT32 i = 0; i < paths; ++i) {
        DISPLAYCONFIG_SOURCE_DEVICE_NAME source{};
        source.header = {DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME, sizeof(source), path[i].sourceInfo.adapterId, path[i].sourceInfo.id};
        if (DisplayConfigGetDeviceInfo(&source.header) != ERROR_SUCCESS || gdi != source.viewGdiDeviceName) continue;
        DISPLAYCONFIG_TARGET_DEVICE_NAME target{};
        target.header = {DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME, sizeof(target), path[i].targetInfo.adapterId, path[i].targetInfo.id};
        if (DisplayConfigGetDeviceInfo(&target.header) == ERROR_SUCCESS) return target.monitorDevicePath;
    }
    return {};
}
BOOL CALLBACK collect(HMONITOR monitor, HDC, LPRECT, LPARAM context) {
    auto& displays = *reinterpret_cast<std::vector<Display>*>(context);
    MONITORINFOEXW mi{};
    mi.cbSize = sizeof(mi);
    if (!GetMonitorInfoW(monitor, &mi)) return TRUE;
    Display display;
    display.monitor = monitor;
    display.desktop = mi.rcMonitor;
    display.gdi_name = mi.szDevice;
    display.id = target_path(display.gdi_name);
    display.persistent_id = !display.id.empty();
    if (!display.persistent_id) display.id = L"session:" + display.gdi_name;
    UINT x = 96, y = 96;
    if (SUCCEEDED(GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, &x, &y))) {
        display.dpi_x = x;
        display.dpi_y = y;
    }
    displays.push_back(std::move(display));
    return TRUE;
}
}

std::vector<Display> enumerate_displays() {
    std::vector<Display> result;
    if (!EnumDisplayMonitors(nullptr, nullptr, collect, reinterpret_cast<LPARAM>(&result))) {
        throw_last_error();
    }
    std::sort(result.begin(), result.end(), [](const auto& a, const auto& b) { return a.id < b.id; });
    return result;
}

struct GpuFrame::State {
    Direct3D11CaptureFrame frame{nullptr};
    com_ptr<ID3D11Texture2D> texture;
    FrameInfo info;
    std::shared_ptr<std::atomic_bool> outstanding;
    ~State() {
        texture = nullptr;
        if (frame) { try { frame.Close(); } catch (...) {} }
        if (outstanding) outstanding->store(false);
    }
};
GpuFrame::GpuFrame() = default;
GpuFrame::GpuFrame(std::unique_ptr<State> state) : state_(std::move(state)) {}
GpuFrame::~GpuFrame() = default;
GpuFrame::GpuFrame(GpuFrame&&) noexcept = default;
GpuFrame& GpuFrame::operator=(GpuFrame&&) noexcept = default;
GpuFrame::operator bool() const { return bool(state_); }
const FrameInfo& GpuFrame::info() const {
    if (!state_) throw std::logic_error("frame lease closed");
    return state_->info;
}
void GpuFrame::with_texture(const std::function<void(ID3D11Texture2D*)>& consumer) const {
    if (!state_) throw std::logic_error("frame lease closed");
    consumer(state_->texture.get());
}
void GpuFrame::close() { state_.reset(); }

struct WgcCapture::Impl {
    Display display;
    std::uint64_t epoch;
    FrameTracker tracker;
    com_ptr<ID3D11Device> device;
    IDirect3DDevice interop{nullptr};
    GraphicsCaptureItem item{nullptr};
    Direct3D11CaptureFramePool pool{nullptr};
    GraphicsCaptureSession session{nullptr};
    std::shared_ptr<std::atomic_bool> outstanding = std::make_shared<std::atomic_bool>(false);
    bool active = false;
    std::wstring adapter;
    Impl(const Display& value, std::uint64_t generation) : display(value), epoch(generation), tracker(generation) {}
    void stop() {
        active = false;
        tracker.invalidate();
        if (session) session.Close();
        session = nullptr;
        if (pool) pool.Close();
        pool = nullptr;
    }
    ~Impl() { try { stop(); } catch (...) {} }
};

WgcCapture::WgcCapture(const Display& display, std::uint64_t epoch, bool floating_point)
    : impl_(std::make_unique<Impl>(display, epoch)) {
    if (!GraphicsCaptureSession::IsSupported()) throw std::runtime_error("WGC unsupported");
    auto& p = *impl_;
    D3D_FEATURE_LEVEL level{};
    check_hresult(D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr,
        D3D11_CREATE_DEVICE_BGRA_SUPPORT, nullptr, 0, D3D11_SDK_VERSION, p.device.put(), &level, nullptr));
    auto dxgi = p.device.as<IDXGIDevice>();
    com_ptr<IDXGIAdapter> adapter;
    check_hresult(dxgi->GetAdapter(adapter.put()));
    DXGI_ADAPTER_DESC desc{};
    check_hresult(adapter->GetDesc(&desc));
    p.adapter = desc.Description;
    com_ptr<IInspectable> inspectable;
    check_hresult(CreateDirect3D11DeviceFromDXGIDevice(dxgi.get(), inspectable.put()));
    p.interop = inspectable.as<IDirect3DDevice>();
    auto factory = get_activation_factory<GraphicsCaptureItem, IGraphicsCaptureItemInterop>();
    check_hresult(factory->CreateForMonitor(display.monitor, guid_of<GraphicsCaptureItem>(), put_abi(p.item)));
    auto format = floating_point ? DirectXPixelFormat::R16G16B16A16Float : DirectXPixelFormat::B8G8R8A8UIntNormalized;
    p.pool = Direct3D11CaptureFramePool::CreateFreeThreaded(p.interop, format, 2, p.item.Size());
    p.session = p.pool.CreateCaptureSession(p.item);
    p.session.IsCursorCaptureEnabled(false);
    // Keep the system capture indicator; borderless permission is a separate product capability.
    p.session.StartCapture();
    p.active = true;
}
WgcCapture::~WgcCapture() = default;
void WgcCapture::stop() {
    if (impl_->outstanding->load()) throw std::logic_error("release frame before stopping capture");
    impl_->stop();
}
std::wstring WgcCapture::adapter_name() const { return impl_->adapter; }
GpuFrame WgcCapture::try_acquire() {
    auto& p = *impl_;
    if (!p.active) throw std::logic_error("capture stopped");
    if (p.outstanding->load()) throw std::logic_error("only one borrowed frame per display is allowed");
    auto frame = p.pool.TryGetNextFrame();
    if (!frame) return {};
    auto state = std::make_unique<GpuFrame::State>();
    state->frame = frame;
    const auto size = frame.ContentSize();
    const auto expected = p.item.Size();
    if (size.Width != expected.Width || size.Height != expected.Height || size.Width <= 0 || size.Height <= 0) {
        throw std::runtime_error("display size changed; recreate capture with a new topology epoch");
    }
    auto access = frame.Surface().as<::Windows::Graphics::DirectX::Direct3D11::IDirect3DDxgiInterfaceAccess>();
    check_hresult(access->GetInterface(__uuidof(ID3D11Texture2D), state->texture.put_void()));
    D3D11_TEXTURE2D_DESC desc{};
    state->texture->GetDesc(&desc);
    if (desc.Width < static_cast<UINT>(size.Width) || desc.Height < static_cast<UINT>(size.Height)) {
        throw std::runtime_error("capture texture smaller than content");
    }
    const auto time = frame.SystemRelativeTime().count();
    if (!p.tracker.accept(p.epoch, time)) return {};
    state->info = {p.display.id, p.epoch, p.tracker.count(), time, size.Width, size.Height, desc.Format};
    p.outstanding->store(true);
    state->outstanding = p.outstanding;
    return GpuFrame(std::move(state));
}
}
