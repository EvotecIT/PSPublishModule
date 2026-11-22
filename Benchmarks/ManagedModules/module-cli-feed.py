"""Build the pinned loopback NuGet v3 feed used by the standalone module CLI benchmark."""

import argparse
import hashlib
import json
from pathlib import Path
import urllib.request
import xml.etree.ElementTree as ET
import zipfile

PACKAGES = (
    ("Pester", "5.7.1", "4a27904c6814a5fbe4758f8e49861f6a1994aee77b71165a5c43c0371ba6c580"),
    ("PSScriptAnalyzer", "1.25.0", "14e634c828eb98efb9f40b2918ba90f139ed5eccdf663a2a747736d996995d60"),
    ("Microsoft.Graph.Users", "2.29.1", "98ba26d6574fb0f03b762f66eb6bebd07290728027d6592d44628a867ca86fc7"),
    ("Microsoft.Graph.Authentication", "2.29.1", "96e7d19f1eadc49d56a921ac136d79ea709d005cd5211be4804235278c4645e6"),
)


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def add_package(feed, base, name, version, expected_hash):
    package_id = name.lower()
    package_file = f"{package_id}.{version}.nupkg"
    flat = feed / "flat" / package_id
    payload = flat / version
    with urllib.request.urlopen(
        f"https://cdn.powershellgallery.com/packages/{package_file}", timeout=120
    ) as response:
        package = response.read()

    digest = hashlib.sha256(package).hexdigest()
    if digest != expected_hash:
        raise ValueError(f"{package_file} SHA-256 changed: {digest}")
    payload.mkdir(parents=True)
    (payload / package_file).write_bytes(package)

    with zipfile.ZipFile(payload / package_file) as archive:
        nuspec = archive.read(next(entry for entry in archive.namelist() if entry.endswith(".nuspec")))
        manifest = next(entry for entry in archive.namelist() if entry.lower() == f"{package_id}.psd1")
        manifest_hash = hashlib.sha256(archive.read(manifest)).hexdigest()

    (payload / f"{package_id}.nuspec").write_bytes(nuspec)
    dependencies = [
        {"id": dep.attrib["id"], "range": dep.attrib.get("version", "")}
        for dep in ET.fromstring(nuspec).iter()
        if dep.tag.split("}")[-1] == "dependency"
    ]
    write_json(flat / "index.json", {"versions": [version]})
    registration_base = f"{base}registration/{package_id}/"
    registration = f"{registration_base}{version}.json"
    leaf = {
        "@id": registration,
        "registration": f"{registration_base}index.json",
        "packageContent": f"{base}flat/{package_id}/{version}/{package_file}",
        "catalogEntry": {
            "@id": f"{registration}#catalog",
            "id": name,
            "version": version,
            "listed": True,
            "published": "2025-01-01T00:00:00Z",
            "dependencyGroups": [{"targetFramework": "", "dependencies": dependencies}],
        },
    }
    registration_directory = feed / "registration" / package_id
    write_json(registration_directory / f"{version}.json", leaf)
    write_json(registration_directory / "index.json", {
        "count": 1,
        "items": [{"@id": f"{registration}#page", "lower": version, "upper": version, "count": 1, "items": [leaf]}],
    })
    return {
        "version": version,
        "manifest": manifest,
        "manifestSha256": manifest_hash,
        "packageSha256": digest,
        "packageBytes": len(package),
        "dependencies": dependencies,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path, help="Directory for the feed and manifest digest file")
    parser.add_argument("--port", type=int, default=8173, help="Port used by the loopback HTTP server")
    args = parser.parse_args()
    base = f"http://127.0.0.1:{args.port}/"
    feed = args.output / "feed"
    feed.mkdir(parents=True)
    proof = {name: add_package(feed, base, name, version, digest) for name, version, digest in PACKAGES}
    write_json(feed / "index.json", {
        "version": "3.0.0",
        "resources": [
            {"@id": f"{base}flat/", "@type": "PackageBaseAddress/3.0.0"},
            {"@id": f"{base}registration/", "@type": "RegistrationsBaseUrl/3.6.0"},
            {"@id": f"{base}registration/", "@type": "RegistrationsBaseUrl/3.0.0-rc"},
        ],
    })
    write_json(args.output / "proof.json", proof)
    print(f"Feed: {base}index.json")


if __name__ == "__main__":
    main()
