#!/usr/bin/env python3

import re
import sys
import zipfile
from pathlib import Path
from xml.etree import ElementTree


EXPECTED_IDS = {
    "Nfs",
    "Nfs.Abstractions",
    "Nfs.Client",
    "Nfs.Mount",
    "Nfs.Nlm",
    "Nfs.Nsm",
    "Nfs.Protocol.V2",
    "Nfs.Protocol.V3",
    "Nfs.Protocol.V4",
    "Nfs.Rpc",
    "Nfs.Server",
    "Nfs.Xdr",
}


def local_name(tag):
    return tag.rsplit("}", 1)[-1]


def read_package(path):
    with zipfile.ZipFile(path) as package:
        nuspecs = [name for name in package.namelist() if name.lower().endswith(".nuspec")]
        if len(nuspecs) != 1:
            raise ValueError(f"{path}: expected exactly one nuspec, found {len(nuspecs)}")

        root = ElementTree.fromstring(package.read(nuspecs[0]))
        metadata = next((node for node in root if local_name(node.tag) == "metadata"), None)
        if metadata is None:
            raise ValueError(f"{path}: nuspec has no metadata element")

        fields = {local_name(node.tag): node.text for node in metadata}
        package_id = fields.get("id")
        version = fields.get("version")
        if not package_id or not version:
            raise ValueError(f"{path}: nuspec must declare package id and version")
        return package_id, version


def main():
    if len(sys.argv) != 3:
        raise SystemExit("usage: verify-packages.py <artifact-directory> <release-tag>")

    artifact_dir = Path(sys.argv[1])
    tag = sys.argv[2]
    match = re.fullmatch(r"v(\d+\.\d+\.\d+)", tag)
    if match is None:
        raise SystemExit(f"Expected a stable vMAJOR.MINOR.PATCH tag, got {tag!r}")
    expected_version = match.group(1)

    packages = sorted(artifact_dir.glob("*.nupkg"))
    if not packages:
        raise SystemExit(f"No .nupkg files found in {artifact_dir}")

    actual = {}
    for path in packages:
        try:
            package_id, version = read_package(path)
        except (OSError, ValueError, zipfile.BadZipFile, ElementTree.ParseError) as error:
            raise SystemExit(str(error)) from error
        if package_id in actual:
            raise SystemExit(f"Duplicate package ID {package_id!r} in {artifact_dir}")
        if version != expected_version:
            raise SystemExit(
                f"{path}: package version {version!r} does not match tag {tag!r}"
            )
        actual[package_id] = version

    missing = EXPECTED_IDS - actual.keys()
    unexpected = actual.keys() - EXPECTED_IDS
    if missing or unexpected:
        problems = []
        if missing:
            problems.append(f"missing package IDs: {', '.join(sorted(missing))}")
        if unexpected:
            problems.append(f"unexpected package IDs: {', '.join(sorted(unexpected))}")
        raise SystemExit("; ".join(problems))

    print(f"Verified {len(actual)} packages at version {expected_version}:")
    for package_id in sorted(actual):
        print(f"  {package_id} {actual[package_id]}")


if __name__ == "__main__":
    main()
