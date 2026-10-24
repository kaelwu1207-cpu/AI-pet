#pragma once
#include <cstdint>

namespace aipet {
// One tracker per display and topology generation. No-frame polls never call accept.
class FrameTracker {
public:
    explicit FrameTracker(std::uint64_t epoch) : epoch_(epoch) {}
    bool accept(std::uint64_t epoch, std::int64_t timestamp) {
        if (!active_ || epoch != epoch_ || timestamp <= last_) return false;
        last_ = timestamp;
        ++count_;
        return true;
    }
    void invalidate() { active_ = false; }
    std::uint64_t count() const { return count_; }
private:
    std::uint64_t epoch_;
    std::int64_t last_ = -1;
    std::uint64_t count_ = 0;
    bool active_ = true;
};
}
