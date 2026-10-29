"""Check whether PCL CE publishes per-version release assets."""

from __future__ import annotations

import json
import ssl
import urllib.request

ctx = ssl._create_unverified_context()
url = "https://api.github.com/repos/PCL-Community/PCL-CE/releases?per_page=5"
req = urllib.request.Request(url, headers={"User-Agent": "pcl-audit/1.0", "Accept": "application/vnd.github+json"})
with urllib.request.urlopen(req, context=ctx, timeout=30) as resp:
    data = json.load(resp)
for rel in data:
    assets = [a["name"] for a in rel.get("assets", [])]
    print(f"{rel['tag_name']:20s} {rel['published_at']}  prerelease={rel['prerelease']}  assets={assets}")
