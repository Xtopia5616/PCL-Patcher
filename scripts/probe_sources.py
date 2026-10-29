"""Probe PCL update/notice endpoints and print status + body head."""

from __future__ import annotations

import ssl
import urllib.error
import urllib.request

URLS = [
    "https://files.mcimirror.top/PCL/",
    "https://files.mcimirror.top/PCL/latest.json",
    "https://files.mcimirror.top/PCL/version.json",
    "https://pcl2-server-1253424809.file.myqcloud.com/notice.cfg",
    "https://pcl2-server-1253424809.file.myqcloud.com/ServerConfig.json",
]

ctx = ssl._create_unverified_context()

for url in URLS:
    req = urllib.request.Request(url, headers={"User-Agent": "pcl-audit/1.0"})
    try:
        with urllib.request.urlopen(req, context=ctx, timeout=30) as resp:
            body = resp.read(4000)
            print(f"== {url}\n   {resp.status} {resp.headers.get('Content-Type')} len={len(body)}")
            print("   " + body[:600].decode("utf-8", "replace").replace("\n", "\n   "))
    except urllib.error.HTTPError as exc:
        print(f"== {url}\n   HTTP {exc.code} {exc.reason}")
    except Exception as exc:  # noqa: BLE001
        print(f"== {url}\n   ERR {type(exc).__name__}: {exc}")
