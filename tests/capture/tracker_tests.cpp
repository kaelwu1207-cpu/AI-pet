#include "frame_tracker.hpp"
#include <iostream>
#include <stdexcept>

static void require(bool value, const char* message) {
    if (!value) throw std::runtime_error(message);
}
int main() {
    try {
        aipet::FrameTracker left(7), right(7);
        require(left.accept(7, 100), "first frame accepted");
        require(!left.accept(7, 100), "duplicate timestamp rejected");
        require(!left.accept(7, 99), "out of order frame rejected");
        require(!left.accept(6, 200), "stale topology rejected");
        require(left.count() == 1, "invalid input must not mutate count");
        require(right.accept(7, 50), "display streams have independent clocks");
        require(left.accept(7, 101), "new frame accepted");
        left.invalidate();
        require(!left.accept(7, 102), "stopped source rejects frames");
        require(left.count() == 2, "stop does not fabricate a frame");
        require(right.accept(7, 51), "one display stopping must not stop another");
        std::cout << "PASS 10 frame identity, ordering and isolation assertions\n";
        return 0;
    } catch (const std::exception& e) {
        std::cerr << "FAIL " << e.what() << '\n';
        return 1;
    }
}
