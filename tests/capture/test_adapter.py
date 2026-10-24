import sys
import unittest
from pathlib import Path
from types import SimpleNamespace

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'src' / 'capture'))
from windows_capture_adapter import CaptureStream


class FakeCapture:
    def __init__(self, **kwargs):
        self.options = kwargs
        self.stops = 0
    def start_free_threaded(self):
        return self
    def stop(self):
        self.stops += 1


class AdapterTests(unittest.TestCase):
    def test_display_streams_are_independent_and_monotonic(self):
        seen = []
        left = CaptureStream(1, 'left', 7, seen.append, factory=FakeCapture)
        right = CaptureStream(2, 'right', 7, seen.append, factory=FakeCapture)
        left.start(); right.start()
        stop = SimpleNamespace(stop=lambda: None)
        for stream, timestamp in [(left, 10), (left, 10), (left, 9), (right, 2), (left, 11)]:
            stream.native.frame_handler(SimpleNamespace(width=20, height=10, timespan=timestamp), stop)
        self.assertEqual([(x.display_id, x.timestamp_100ns) for x in seen], [('left', 10), ('right', 2), ('left', 11)])
        self.assertEqual(left.snapshot()['rejected_frames'], 2)
        left.stop(); right.stop()

    def test_callback_failure_stops_only_its_source(self):
        def bad(_):
            raise RuntimeError('consumer failed')
        left = CaptureStream(1, 'left', 7, bad, factory=FakeCapture)
        left.start()
        stopped = []
        left.native.frame_handler(SimpleNamespace(width=20, height=10, timespan=1), SimpleNamespace(stop=lambda: stopped.append(True)))
        self.assertEqual(stopped, [True])
        self.assertIn('consumer failed', left.snapshot()['error'])
        left.stop()

    def test_stopped_stream_never_delivers_late_frames(self):
        seen = []
        stream = CaptureStream(1, 'left', 7, seen.append, factory=FakeCapture)
        stream.start(); stream.stop(); stream.stop()
        stream.native.frame_handler(SimpleNamespace(width=20, height=10, timespan=1), SimpleNamespace(stop=lambda: None))
        self.assertFalse(seen)
        self.assertEqual(stream.native.stops, 1)

    def test_invalid_target_rejected_before_opening_capture(self):
        with self.assertRaises(ValueError):
            CaptureStream(0, 'left', 7, lambda _: None, factory=FakeCapture)


if __name__ == '__main__':
    unittest.main()
