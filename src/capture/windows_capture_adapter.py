"""Thin integration of windows-capture; capture, devices and buffers stay upstream.

The Python API maps GPU frames to CPU memory. This adapter does not claim a
GPU-only pipeline. Use the upstream Rust Frame::as_raw_texture for that path.
Consumers must finish using packet.frame before their callback returns.
"""
from dataclasses import dataclass
from threading import RLock
from typing import Any, Callable


@dataclass(frozen=True)
class FramePacket:
    display_id: str
    topology_epoch: int
    sequence: int
    timestamp_100ns: int
    width: int
    height: int
    frame: Any  # Borrowed upstream frame; deliberately no persistent image cache.


class CaptureStream:
    def __init__(self, monitor_index: int, display_id: str, epoch: int,
                 consumer: Callable[[FramePacket], None], *, factory=None):
        if monitor_index < 1 or not display_id or epoch < 0:
            raise ValueError('one-based monitor index, display identity and epoch required')
        if factory is None:
            from windows_capture import WindowsCapture
            factory = WindowsCapture
        self.native = factory(monitor_index=monitor_index, cursor_capture=False,
                              minimum_update_interval=100, draw_border=None)
        self.native.frame_handler = self._on_frame
        self.native.closed_handler = self._on_closed
        self.display_id, self.epoch = display_id, epoch
        self.consumer = consumer
        self._lock = RLock()
        self._control = None
        self._started = False
        self._closed = False
        self._last_time = -1
        self._count = self._rejected = 0
        self._width = self._height = 0
        self._error = ''

    def start(self):
        if self._started or self._closed:
            raise RuntimeError('create a new stream for restart or topology changes')
        self._started = True
        try:
            self._control = self.native.start_free_threaded()
        except Exception as error:
            with self._lock:
                self._closed, self._error = True, str(error)
            raise

    def _on_frame(self, frame, control):
        with self._lock:
            if self._closed:
                return
            timestamp = frame.timespan
            if timestamp <= self._last_time or frame.width <= 0 or frame.height <= 0:
                self._rejected += 1
                return
            try:
                packet = FramePacket(self.display_id, self.epoch, self._count + 1,
                                     timestamp, frame.width, frame.height, frame)
                self.consumer(packet)
                self._last_time = timestamp
                self._width, self._height = frame.width, frame.height
                self._count += 1
            except Exception as error:
                self._error, self._closed = str(error), True
                control.stop()

    def _on_closed(self):
        with self._lock:
            self._closed = True

    def stop(self):
        # Do not hold the callback lock while joining the upstream worker.
        with self._lock:
            self._closed = True
            control, self._control = self._control, None
        if control is not None:
            control.stop()

    def snapshot(self):
        with self._lock:
            return dict(display_id=self.display_id, topology_epoch=self.epoch,
                        frames=self._count, rejected_frames=self._rejected,
                        width=self._width, height=self._height,
                        last_timestamp_100ns=self._last_time,
                        closed=self._closed, error=self._error)
