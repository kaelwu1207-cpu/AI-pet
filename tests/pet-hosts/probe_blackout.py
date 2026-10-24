"""Verify only the owned fixture ROI using upstream WGC; never save a frame."""
import ctypes
import json
import sys
import threading
from windows_capture import WindowsCapture

if ctypes.windll.user32.GetSystemMetrics(80) != 1:
    raise SystemExit('This bounded fixture probe currently requires exactly one physical display')
x, y, width, height = map(int, sys.argv[1:])
done = threading.Event()
state = dict(frames=0, black_inside=False, white_outside=False, pixels_persisted=False,
             source='windows-capture WGC', consumer='CPU test-only ROI inspection')
capture = WindowsCapture(monitor_index=1, cursor_capture=False, minimum_update_interval=100)

def arrived(frame, control):
    pixels = frame.frame_buffer
    inside = pixels[y+5:y+height-5, x+5:x+width-5, :3]
    outside = pixels[y-15:y-5, x+5:x+15, :3]
    state['frames'] += 1
    state['black_inside'] = bool(inside.size and inside.max() <= 2)
    state['white_outside'] = bool(outside.size and outside.min() >= 245)
    if state['black_inside'] and state['white_outside']:
        done.set()

capture.frame_handler = arrived
capture.closed_handler = done.set
control = capture.start_free_threaded()
try:
    done.wait(2)
finally:
    control.stop()
state['passed'] = state['black_inside'] and state['white_outside']
print(json.dumps(state))
raise SystemExit(0 if state['passed'] else 1)
