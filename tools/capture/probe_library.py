"""Bounded upstream-library test; output metadata only, never pixels."""
import argparse
import ctypes
import importlib.metadata
import json
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'src' / 'capture'))
from windows_capture_adapter import CaptureStream


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--seconds', type=int, default=4, choices=range(2, 16))
    parser.add_argument('--min-displays', type=int, default=2, choices=range(1, 17))
    args = parser.parse_args()
    count = ctypes.windll.user32.GetSystemMetrics(80)
    epoch = time.monotonic_ns()
    streams, errors = [], []
    try:
        for index in range(1, count + 1):
            try:
                stream = CaptureStream(index, f'session-monitor-{index}', epoch, lambda packet: None)
                streams.append(stream)
                stream.start()
            except Exception as error:
                errors.append(dict(monitor_index=index, error=str(error)))
        time.sleep(args.seconds)
    finally:
        for stream in streams:
            try:
                stream.stop()
            except Exception as error:
                errors.append(dict(display_id=stream.display_id, error=str(error)))
    results = [stream.snapshot() for stream in streams]
    passed = (count >= args.min_displays and len(results) == count and not errors
              and all(x['frames'] >= 3 and not x['error'] for x in results))
    output = dict(schema_version='aipet.capture.library-probe/0.1',
                  library='windows-capture', version=importlib.metadata.version('windows-capture'),
                  backend='upstream WGC', consumer_memory='CPU-mapped Python API',
                  display_count=count, minimum_required=args.min_displays,
                  topology_epoch=epoch, topology_identity='session-only; stable identity adapter pending',
                  displays=results, errors=errors, pixels_persisted=False,
                  external_upload=False, hdr_verified=False,
                  passed=passed, exit_code=0 if passed else 2)
    print(json.dumps(output, ensure_ascii=False))
    return output['exit_code']


if __name__ == '__main__':
    raise SystemExit(main())
