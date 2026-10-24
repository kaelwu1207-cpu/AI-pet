#include "wgc_capture.hpp"
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.Data.Json.h>
#include <algorithm>
#include <chrono>
#include <iostream>
#include <thread>
#include <stdexcept>

using namespace winrt;
using namespace Windows::Data::Json;
using namespace std::chrono;

namespace {
void put(JsonObject& json, const wchar_t* name, const std::wstring& value) {
    json.Insert(name, JsonValue::CreateStringValue(value));
}
void number(JsonObject& json, const wchar_t* name, double value) {
    json.Insert(name, JsonValue::CreateNumberValue(value));
}
void set_bool(JsonObject& json, const wchar_t* name, bool value) {
    json.Insert(name, JsonValue::CreateBooleanValue(value));
}
std::wstring error_text() {
    try { throw; }
    catch (const hresult_error& e) { return L"HRESULT " + std::to_wstring(e.code().value) + L": " + e.message().c_str(); }
    catch (const std::exception& e) { return to_hstring(e.what()).c_str(); }
}
class Marker {
public:
    explicit Marker(const aipet::Display& display) {
        WNDCLASSW wc{};
        wc.lpfnWndProc = procedure;
        wc.hInstance = GetModuleHandleW(nullptr);
        wc.lpszClassName = L"AIpetCaptureMarker";
        if (!RegisterClassW(&wc) && GetLastError() != ERROR_CLASS_ALREADY_EXISTS) throw_last_error();
        window_ = CreateWindowExW(WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT,
            wc.lpszClassName, L"AI-pet capture validation", WS_POPUP,
            display.desktop.right - 144, display.desktop.top + 64, 96, 96,
            nullptr, nullptr, wc.hInstance, this);
        if (!window_) throw_last_error();
        SetWindowPos(window_, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }
    ~Marker() { if (window_) DestroyWindow(window_); }
    void paint(unsigned phase, unsigned index) {
        const auto next = (phase & 1) ? RGB(220, 40 + index * 15, 20) : RGB(20, 50, 210 - index * 5);
        if (next == color_) return;
        color_ = next;
        InvalidateRect(window_, nullptr, FALSE);
        UpdateWindow(window_);
    }
    RECT bounds() const { RECT value{}; GetWindowRect(window_, &value); return value; }
private:
    static LRESULT CALLBACK procedure(HWND window, UINT message, WPARAM wparam, LPARAM lparam) {
        if (message == WM_NCCREATE) {
            auto create = reinterpret_cast<CREATESTRUCTW*>(lparam);
            SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(create->lpCreateParams));
        }
        auto self = reinterpret_cast<Marker*>(GetWindowLongPtrW(window, GWLP_USERDATA));
        if (message == WM_PAINT && self) {
            PAINTSTRUCT ps{};
            auto dc = BeginPaint(window, &ps);
            RECT rect{}; GetClientRect(window, &rect);
            auto brush = CreateSolidBrush(self->color_);
            FillRect(dc, &rect, brush); DeleteObject(brush);
            EndPaint(window, &ps);
            return 0;
        }
        return DefWindowProcW(window, message, wparam, lparam);
    }
    HWND window_ = nullptr;
    COLORREF color_ = RGB(0, 0, 0);
};

std::uint64_t sample_texture(ID3D11Texture2D* texture, int x, int y) {
    com_ptr<ID3D11Device> device;
    texture->GetDevice(device.put());
    com_ptr<ID3D11DeviceContext> context;
    device->GetImmediateContext(context.put());
    D3D11_TEXTURE2D_DESC desc{};
    texture->GetDesc(&desc);
    desc.Width = 16;
    desc.Height = 16;
    desc.MipLevels = 1;
    desc.ArraySize = 1;
    desc.SampleDesc = {1, 0};
    desc.Usage = D3D11_USAGE_STAGING;
    desc.BindFlags = 0;
    desc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    desc.MiscFlags = 0;
    com_ptr<ID3D11Texture2D> staging;
    check_hresult(device->CreateTexture2D(&desc, nullptr, staging.put()));
    D3D11_BOX box{static_cast<UINT>(x), static_cast<UINT>(y), 0,
        static_cast<UINT>(x + 16), static_cast<UINT>(y + 16), 1};
    context->CopySubresourceRegion(staging.get(), 0, 0, 0, 0, texture, 0, &box);
    D3D11_MAPPED_SUBRESOURCE mapped{};
    check_hresult(context->Map(staging.get(), 0, D3D11_MAP_READ, 0, &mapped));
    std::uint64_t hash = 14695981039346656037ull;
    const unsigned bytes = desc.Format == DXGI_FORMAT_R16G16B16A16_FLOAT ? 8 : 4;
    for (unsigned row = 0; row < 16; ++row) {
        auto data = static_cast<const unsigned char*>(mapped.pData) + row * mapped.RowPitch;
        for (unsigned col = 0; col < 16 * bytes; ++col) hash = (hash ^ data[col]) * 1099511628211ull;
    }
    context->Unmap(staging.get(), 0);
    return hash; // Used in memory only. Neither pixels nor hashes are persisted.
}
bool same_topology(const std::vector<aipet::Display>& before, const std::vector<aipet::Display>& after) {
    if (before.size() != after.size()) return false;
    for (size_t i = 0; i < before.size(); ++i) {
        if (before[i].id != after[i].id || before[i].monitor != after[i].monitor ||
            !EqualRect(&before[i].desktop, &after[i].desktop) ||
            before[i].dpi_x != after[i].dpi_x || before[i].dpi_y != after[i].dpi_y) return false;
    }
    return true;
}
struct Stream {
    std::unique_ptr<aipet::WgcCapture> capture;
    std::unique_ptr<Marker> marker;
    std::wstring error, adapter;
    std::uint64_t frames = 0, changes = 0, last_hash = 0, empty_polls = 0;
    std::int64_t first_time = 0, last_time = 0;
    int width = 0, height = 0;
    DXGI_FORMAT format = DXGI_FORMAT_UNKNOWN;
    bool lifetime_checked = false;
};
}

int wmain(int argc, wchar_t** argv) {
    init_apartment(apartment_type::multi_threaded);
    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    JsonObject receipt;
    put(receipt, L"schemaVersion", L"aipet.capture.probe/0.1");
    put(receipt, L"backend", L"WGC/D3D11-hardware");
    set_bool(receipt, L"pixelsPersisted", false);
    set_bool(receipt, L"externalUpload", false);
    int exit_code = 1;
    try {
        unsigned seconds = 5, minimum = 2;
        bool stimulus = false, floating_point = true;
        for (int i = 1; i < argc; ++i) {
            std::wstring arg = argv[i];
            if (arg == L"--stimulus") stimulus = true;
            else if (arg == L"--seconds" && i + 1 < argc) seconds = std::stoul(argv[++i]);
            else if (arg == L"--min-displays" && i + 1 < argc) minimum = std::stoul(argv[++i]);
            else if (arg == L"--bgra8") floating_point = false;
            else throw std::invalid_argument("unknown/incomplete argument");
        }
        if (seconds < 2 || seconds > 30 || minimum < 1 || minimum > 16) throw std::invalid_argument("probe budget outside allowed range");
        const auto displays = aipet::enumerate_displays();
        const auto epoch = GetTickCount64();
        number(receipt, L"topologyEpoch", static_cast<double>(epoch));
        number(receipt, L"displayCount", static_cast<double>(displays.size()));
        number(receipt, L"minimumRequired", minimum);
        number(receipt, L"durationSeconds", seconds);
        set_bool(receipt, L"stimulus", stimulus);
        set_bool(receipt, L"actualHdrDisplayVerified", false);
        std::vector<Stream> streams(displays.size());
        for (size_t i = 0; i < displays.size(); ++i) {
            try {
                if (displays[i].desktop.right - displays[i].desktop.left < 256 || displays[i].desktop.bottom - displays[i].desktop.top < 256)
                    throw std::runtime_error("display too small for probe ROI");
                streams[i].capture = std::make_unique<aipet::WgcCapture>(displays[i], epoch, floating_point);
                streams[i].adapter = streams[i].capture->adapter_name();
                if (stimulus) streams[i].marker = std::make_unique<Marker>(displays[i]);
            } catch (...) { streams[i].error = error_text(); }
        }
        const auto started = steady_clock::now();
        while (steady_clock::now() - started < std::chrono::seconds(seconds)) {
            const auto elapsed = duration_cast<milliseconds>(steady_clock::now() - started).count();
            for (size_t i = 0; i < streams.size(); ++i) {
                auto& stream = streams[i];
                if (!stream.capture || !stream.error.empty()) continue;
                try {
                    if (stream.marker) stream.marker->paint(static_cast<unsigned>(elapsed / (120 + i * 47)), static_cast<unsigned>(i));
                    auto lease = stream.capture->try_acquire();
                    if (!lease) { ++stream.empty_polls; continue; }
                    const auto info = lease.info();
                    if (info.display_id != displays[i].id || info.epoch != epoch || info.timestamp_100ns <= stream.last_time)
                        throw std::runtime_error("frame identity or time invariant violated");
                    ++stream.frames;
                    if (stream.frames == 1) stream.first_time = info.timestamp_100ns;
                    stream.last_time = info.timestamp_100ns;
                    stream.width = info.width; stream.height = info.height; stream.format = info.format;
                    std::uint64_t hash = 0;
                    int sample_x = info.width - 112, sample_y = 96;
                    if (stream.marker) {
                        auto rect = stream.marker->bounds();
                        sample_x = rect.left - displays[i].desktop.left + 32;
                        sample_y = rect.top - displays[i].desktop.top + 32;
                    }
                    if (sample_x < 0 || sample_y < 0 || sample_x + 16 > info.width || sample_y + 16 > info.height)
                        throw std::runtime_error("marker ROI outside captured frame");
                    lease.with_texture([&](auto texture) { hash = sample_texture(texture, sample_x, sample_y); });
                    if (stream.frames > 1 && hash != stream.last_hash) ++stream.changes;
                    stream.last_hash = hash;
                    if (!stream.lifetime_checked) {
                        bool outstanding_rejected = false, closed_rejected = false;
                        try { auto extra = stream.capture->try_acquire(); }
                        catch (const std::logic_error&) { outstanding_rejected = true; }
                        auto moved = std::move(lease);
                        if (lease || !moved) throw std::runtime_error("lease move failed");
                        moved.close();
                        try { moved.with_texture([](auto) {}); }
                        catch (const std::logic_error&) { closed_rejected = true; }
                        stream.lifetime_checked = outstanding_rejected && closed_rejected;
                        if (!stream.lifetime_checked) throw std::runtime_error("lease lifetime invariant failed");
                    }
                } catch (...) { stream.error = error_text(); }
            }
            MSG msg{};
            while (PeekMessageW(&msg, nullptr, 0, 0, PM_REMOVE)) { TranslateMessage(&msg); DispatchMessageW(&msg); }
            std::this_thread::sleep_for(milliseconds(25));
        }
        JsonArray results;
        bool passed = displays.size() >= minimum;
        for (size_t i = 0; i < streams.size(); ++i) {
            auto& stream = streams[i];
            stream.marker.reset();
            if (stream.capture) {
                try {
                    stream.capture->stop();
                    bool rejected = false;
                    try { auto extra = stream.capture->try_acquire(); }
                    catch (const std::logic_error&) { rejected = true; }
                    if (!rejected) throw std::runtime_error("stopped capture accepted a request");
                } catch (...) { stream.error = error_text(); }
            }
            stream.capture.reset();
            const bool ok = stream.error.empty() && stream.frames >= 3 && stream.lifetime_checked && (!stimulus || stream.changes >= 2);
            passed = passed && ok;
            JsonObject result;
            put(result, L"displayId", displays[i].id);
            put(result, L"gdiName", displays[i].gdi_name);
            set_bool(result, L"persistentId", displays[i].persistent_id);
            put(result, L"adapter", stream.adapter);
            number(result, L"left", displays[i].desktop.left); number(result, L"top", displays[i].desktop.top);
            number(result, L"dpiX", displays[i].dpi_x); number(result, L"dpiY", displays[i].dpi_y);
            number(result, L"width", stream.width); number(result, L"height", stream.height);
            number(result, L"dxgiFormat", stream.format);
            number(result, L"frames", static_cast<double>(stream.frames));
            number(result, L"roiChanges", static_cast<double>(stream.changes));
            number(result, L"emptyPolls", static_cast<double>(stream.empty_polls));
            number(result, L"firstTimestamp100ns", static_cast<double>(stream.first_time));
            number(result, L"lastTimestamp100ns", static_cast<double>(stream.last_time));
            set_bool(result, L"leaseChecksPassed", stream.lifetime_checked);
            set_bool(result, L"passed", ok);
            put(result, L"error", stream.error);
            results.Append(result);
        }
        const bool topology_stable = same_topology(displays, aipet::enumerate_displays());
        passed = passed && topology_stable;
        receipt.Insert(L"displays", results);
        set_bool(receipt, L"topologyStable", topology_stable);
        set_bool(receipt, L"captureResourcesReleased", true);
        put(receipt, L"outcome", passed ? L"passed" : L"failed_or_insufficient_displays");
        exit_code = passed ? 0 : 2;
    } catch (...) { put(receipt, L"error", error_text()); put(receipt, L"outcome", L"failed"); }
    number(receipt, L"exitCode", exit_code);
    std::cout << to_string(receipt.Stringify()) << '\n';
    return exit_code;
}
