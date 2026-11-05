"""Generate the small, code-drawn fallback window icon (no external packages)."""
from pathlib import Path
import struct
import zlib

size = 64
pixels = bytearray(size * size * 4)


def rectangle(x1, y1, x2, y2, color):
    for y in range(y1, y2):
        for x in range(x1, x2):
            index = (y * size + x) * 4
            pixels[index:index + 4] = bytes(color)


rectangle(6, 6, 48, 44, (65, 93, 135, 255))
rectangle(9, 15, 45, 41, (213, 230, 251, 255))
rectangle(18, 22, 60, 58, (22, 109, 186, 255))
rectangle(21, 31, 57, 55, (239, 248, 255, 255))
rectangle(25, 37, 51, 40, (22, 109, 186, 255))
rectangle(25, 45, 44, 48, (22, 109, 186, 255))


def chunk(kind, data):
    return struct.pack('!I', len(data)) + kind + data + struct.pack('!I', zlib.crc32(kind + data))


raw = b''.join(b'\0' + pixels[y * size * 4:(y + 1) * size * 4] for y in range(size))
image = b'\x89PNG\r\n\x1a\n'
image += chunk(b'IHDR', struct.pack('!2I5B', size, size, 8, 6, 0, 0, 0))
image += chunk(b'IDAT', zlib.compress(raw)) + chunk(b'IEND', b'')
destination = Path(__file__).resolve().parents[1] / 'src/PowerToyWalker/Images/walker.png'
destination.parent.mkdir(parents=True, exist_ok=True)
destination.write_bytes(image)
