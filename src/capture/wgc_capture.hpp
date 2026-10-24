#pragma once
#include <windows.h>
#include <d3d11.h>
#include <cstdint>
#include <functional>
#include <memory>
#include <string>
#include <vector>

namespace aipet {
struct Display {
    std::wstring id; // DisplayConfig target path; session-only fallback is explicitly marked.
    std::wstring gdi_name;
    bool persistent_id = false;
    HMONITOR monitor = nullptr;
    RECT desktop{}; // Physical desktop pixels, can be negative.
    UINT dpi_x = 96, dpi_y = 96;
};
std::vector<Display> enumerate_displays();

struct FrameInfo {
    std::wstring display_id;
    std::uint64_t epoch = 0, sequence = 0;
    std::int64_t timestamp_100ns = 0;
    int width = 0, height = 0;
    DXGI_FORMAT format = DXGI_FORMAT_UNKNOWN;
};
// Move-only lease. Texture access is valid ONLY inside the callback and while this lease lives.
class GpuFrame {
public:
    struct State;
    GpuFrame();
    explicit GpuFrame(std::unique_ptr<State> state);
    ~GpuFrame();
    GpuFrame(GpuFrame&&) noexcept;
    GpuFrame& operator=(GpuFrame&&) noexcept;
    GpuFrame(const GpuFrame&) = delete;
    explicit operator bool() const;
    const FrameInfo& info() const;
    void with_texture(const std::function<void(ID3D11Texture2D*)>& consumer) const;
    void close();
private:
    std::unique_ptr<State> state_;
};

class WgcCapture {
public:
    WgcCapture(const Display& display, std::uint64_t epoch, bool floating_point = true);
    ~WgcCapture();
    WgcCapture(const WgcCapture&) = delete;
    GpuFrame try_acquire(); // Empty means no frame; failure throws, never fabricates a frame.
    void stop();
    std::wstring adapter_name() const;
private:
    struct Impl;
    std::unique_ptr<Impl> impl_;
};
}
