"""Extracts the layers of a Paint.NET .pdn file to PNGs, with no Paint.NET installed.

The format is undocumented but simple enough to read directly:

    "PDN3" | 3-byte little-endian header length | XML header | .NET BinaryFormatter stream

The XML header carries only the canvas size and layer count. Everything else — layer names,
properties and pixel data — lives in the serialized stream. Rather than implement a
BinaryFormatter reader, this takes the two things that are unambiguous in it:

  * layer names, as length-prefixed UTF-8 runs next to the "PaintDotNet.Layer" type marker;
  * pixel data, as the GZip members that Paint.NET's MemoryBlock writes in fixed 256KiB chunks.

The chunk count per layer follows from the canvas size, so the members group into layers by
arithmetic rather than by guesswork, and the script checks that arithmetic against the file before
it writes anything. Pixels are BGRA with straight (non-premultiplied) alpha.

Usage:
    python scripts/pdn_layers.py INPUT.pdn OUTPUT_PREFIX
        -> OUTPUT_PREFIX-<layername>.png for each layer, lowest layer first
"""
from __future__ import annotations

import argparse
import re
import zlib
from pathlib import Path

import numpy as np
from PIL import Image

CHUNK = 256 * 1024


def read_pdn(path: Path):
    data = path.read_bytes()
    if data[:4] != b"PDN3":
        raise ValueError(f"{path} is not a PDN3 file")

    header_len = int.from_bytes(data[4:7], "little")
    header = data[7:7 + header_len].decode("utf-8", "replace")
    width = int(re.search(r'width="(\d+)"', header).group(1))
    height = int(re.search(r'height="(\d+)"', header).group(1))
    count = int(re.search(r'layers="(\d+)"', header).group(1))
    body = data[7 + header_len:]
    return body, width, height, count


def layer_names(body: bytes, count: int, limit: int) -> list[str]:
    """Names in file order, or Layer1..N when they cannot be read.

    Anchored on the BinaryFormatter record that actually holds them: BinaryObjectString, which is
    the tag 0x06, a 4-byte object id, a 7-bit-encoded length and then UTF-8. Searching for bare
    length-prefixed text instead matches half the stream — the first attempt at this returned the
    serializer's own field names.

    Records carrying markup or a `$`-prefixed metadata key are the file's EXIF block, so they are
    dropped; what remains before the pixel data is the layer list.
    """
    found: list[str] = []
    record = re.compile(b"\x06(.{4})([\x01-\x7f])", re.S)
    for match in record.finditer(body[:limit]):
        length = match.group(2)[0]
        raw = body[match.end():match.end() + length]
        try:
            text = raw.decode("utf-8")
        except UnicodeDecodeError:
            continue
        if len(text) != length or not text.isprintable():
            continue
        if text.startswith("$") or text.startswith("<"):
            continue
        found.append(text)

    return found[-count:] if len(found) >= count else [f"Layer{i + 1}" for i in range(count)]


def extract(path: Path, prefix: Path) -> list[Path]:
    body, width, height, count = read_pdn(path)

    surface = width * height * 4
    per_layer, remainder = divmod(surface, CHUNK)
    per_layer += 1 if remainder else 0

    starts = [m.start() for m in re.finditer(b"\x1f\x8b\x08", body)]
    if len(starts) != per_layer * count:
        raise ValueError(
            f"expected {per_layer * count} gzip members ({count} layers x {per_layer} chunks), "
            f"found {len(starts)}")

    blocks = []
    for start in starts:
        blocks.append(zlib.decompressobj(16 + zlib.MAX_WBITS).decompress(body[start:]))

    names = layer_names(body, count, starts[0])
    written = []
    for index in range(count):
        raw = b"".join(blocks[index * per_layer:(index + 1) * per_layer])
        if len(raw) != surface:
            raise ValueError(f"layer {index} is {len(raw)} bytes, expected {surface}")

        bgra = np.frombuffer(raw, dtype=np.uint8).reshape(height, width, 4)
        rgba = bgra[:, :, [2, 1, 0, 3]]
        name = names[index] if index < len(names) else f"Layer{index + 1}"
        safe = re.sub(r"[^A-Za-z0-9_-]", "_", name)
        out = prefix.with_name(f"{prefix.name}-{safe}.png")
        Image.fromarray(rgba, "RGBA").save(out)
        opaque = int((rgba[:, :, 3] > 8).sum())
        print(f"  {name:>10} -> {out.name}  ({opaque} visible px)")
        written.append(out)

    return written


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("input")
    parser.add_argument("prefix", help="output path stem, e.g. out/lust-crest")
    args = parser.parse_args()

    path = Path(args.input)
    prefix = Path(args.prefix)
    prefix.parent.mkdir(parents=True, exist_ok=True)
    print(f"{path}:")
    extract(path, prefix)


if __name__ == "__main__":
    main()
