"""Adds the current build to a Jellyfin plugin repository manifest.

Jellyfin reads the manifest as a JSON array of plugins, each with a list of
versions. A version entry needs the download URL, the MD5 checksum of the zip,
the target ABI and a timestamp.
"""

import argparse
import hashlib
import json
import os
from datetime import datetime, timezone

import yaml


def md5_of(path: str) -> str:
    digest = hashlib.md5()
    with open(path, "rb") as stream:
        for chunk in iter(lambda: stream.read(65536), b""):
            digest.update(chunk)
    return digest.hexdigest()


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--build-yaml", required=True)
    parser.add_argument("--manifest", required=True)
    parser.add_argument("--zip", required=True)
    parser.add_argument("--url", required=True)
    args = parser.parse_args()

    with open(args.build_yaml, encoding="utf-8") as stream:
        meta = yaml.safe_load(stream)

    manifest = []
    if os.path.exists(args.manifest):
        with open(args.manifest, encoding="utf-8") as stream:
            manifest = json.load(stream)

    plugin = next((p for p in manifest if p["guid"] == meta["guid"]), None)
    if plugin is None:
        plugin = {"guid": meta["guid"], "versions": []}
        manifest.append(plugin)

    plugin.update(
        {
            "name": meta["name"],
            "description": meta["description"].strip(),
            "overview": meta["overview"],
            "owner": meta["owner"],
            "category": meta["category"],
        }
    )

    version = str(meta["version"])
    plugin["versions"] = [v for v in plugin["versions"] if v["version"] != version]
    plugin["versions"].insert(
        0,
        {
            "version": version,
            "changelog": meta["changelog"].strip(),
            "targetAbi": str(meta["targetAbi"]),
            "sourceUrl": args.url,
            "checksum": md5_of(args.zip),
            "timestamp": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        },
    )

    with open(args.manifest, "w", encoding="utf-8") as stream:
        json.dump(manifest, stream, ensure_ascii=False, indent=2)


if __name__ == "__main__":
    main()
