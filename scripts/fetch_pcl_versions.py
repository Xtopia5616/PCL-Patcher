"""Download official PCL builds from GitHub tag snapshots.

Each tag of Meloong-Git/PCL contains the shipped binary as
``最新正式版.zip`` in the repository root (see the mcmod guide:
https://www.mcmod.cn/post/6883.html).  This script pulls the ones we care
about into ``.versions/<tag>/`` and prints what is inside.
"""

from __future__ import annotations

import hashlib
import io
import ssl
import sys
import urllib.parse
import urllib.request
import zipfile
from pathlib import Path

RAW = "https://raw.githubusercontent.com/Meloong-Git/PCL/{ref}/%E6%9C%80%E6%96%B0%E6%AD%A3%E5%BC%8F%E7%89%88.zip"
REFS = sys.argv[1:] or ["2.99.0.0", "main", "2.14.0.0"]
OUT = Path(__file__).resolve().parent.parent / ".versions"

ctx = ssl._create_unverified_context()


def fetch(url: str) -> bytes:
    req = urllib.request.Request(url, headers={"User-Agent": "pcl-version-audit/1.0"})
    with urllib.request.urlopen(req, context=ctx, timeout=120) as resp:
        return resp.read()


def main() -> int:
    OUT.mkdir(exist_ok=True)
    for ref in REFS:
        url = RAW.format(ref=urllib.parse.quote(ref, safe=""))
        try:
            blob = fetch(url)
        except Exception as exc:  # noqa: BLE001 - report and continue
            print(f"[fail] {ref}: {exc}")
            continue
        digest = hashlib.sha256(blob).hexdigest()
        zip_path = OUT / f"{ref.replace('/', '_')}.zip"
        zip_path.write_bytes(blob)
        print(f"[ok]   {ref}: {len(blob):,} bytes  sha256={digest}")
        with zipfile.ZipFile(io.BytesIO(blob)) as zf:
            for info in zf.infolist():
                print(f"         {info.filename}  {info.file_size:,}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
