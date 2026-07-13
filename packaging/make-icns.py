#!/usr/bin/env python3
"""Assemble a valid macOS .icns from PNG files, no external tools needed.

ImageMagick's `convert x.png x.icns` frequently produces an .icns that Finder
refuses to render. The ICNS container is trivial, though: a 'icns' magic, a
big-endian total length, then a sequence of (4-byte OSType, 4-byte chunk length
including the 8-byte header, data) records. Modern macOS (10.7+) reads
PNG-encoded entries directly, so we just wrap each pre-scaled PNG in the record
whose OSType matches its pixel size.

Usage: make-icns.py <out.icns> <size:png> [<size:png> ...]
"""
import struct
import sys

# OSType for a PNG chunk at each pixel size. macOS picks the best-fit entry, so
# supplying the standard ladder (with retina @2x variants) covers every context
# from the 16px list view to the 1024px App Store tile.
TYPE_FOR_SIZE = {
    16: b"icp4",
    32: b"icp5",
    64: b"icp6",
    128: b"ic07",
    256: b"ic08",
    512: b"ic09",
    1024: b"ic10",
}


def main(argv):
    out = argv[1]
    chunks = []
    for spec in argv[2:]:
        size_s, path = spec.split(":", 1)
        ostype = TYPE_FOR_SIZE[int(size_s)]
        with open(path, "rb") as fh:
            data = fh.read()
        chunks.append(ostype + struct.pack(">I", len(data) + 8) + data)

    body = b"".join(chunks)
    with open(out, "wb") as fh:
        fh.write(b"icns" + struct.pack(">I", len(body) + 8) + body)


if __name__ == "__main__":
    main(sys.argv)
