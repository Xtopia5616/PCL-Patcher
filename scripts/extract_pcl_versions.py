"""Extract the shipped PCL exe out of each snapshot and print SHA-256."""

from __future__ import annotations

import hashlib
import zipfile
from pathlib import Path

BASE = Path(__file__).resolve().parent.parent / ".versions"
TARGETS = {
    "2.99.0.0": "2.99.0.0",
    "main": "2.99.0.1",
    "2.14.0.0": "2.14.0.0",
}

for ref, label in TARGETS.items():
    archive = BASE / f"{ref.replace('/', '_')}.zip"
    out_dir = BASE / label
    out_dir.mkdir(exist_ok=True)
    with zipfile.ZipFile(archive) as zf:
        zf.extractall(out_dir)
    exe = out_dir / "Plain Craft Launcher 2.exe"
    blob = exe.read_bytes()
    print(f"{label:8s} {len(blob):>10,}  sha256={hashlib.sha256(blob).hexdigest()}")
