#!/usr/bin/env python3
"""Import a Polycam Images ZIP without changing the original image bytes.

Usage: import-reference-photos.py ARCHIVE DATASET_DIRECTORY CAPTURE_ID
The dataset directory contains its Red-Point model and a photos/ subdirectory.
"""

import argparse
import hashlib
import json
import pathlib
import shutil
import tempfile
import zipfile


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def import_photos(archive_path, directory, capture_id):
    extensions = {".jpg", ".jpeg", ".png", ".tif", ".tiff", ".heic", ".heif"}
    directory.mkdir(parents=True, exist_ok=True)
    destination = directory / "photos"
    inventory = directory / "source-photos.json"
    if destination.exists() or inventory.exists():
        raise ValueError("The dataset already has source photos; refusing to replace them.")
    with tempfile.TemporaryDirectory(prefix=".photo-import-", dir=directory) as temporary:
        staging = pathlib.Path(temporary)
        photos = []
        names = set()
        with zipfile.ZipFile(archive_path) as archive:
            for entry in archive.infolist():
                relative = pathlib.PurePosixPath(entry.filename)
                if entry.is_dir() or "__MACOSX" in relative.parts or relative.name.startswith("._"):
                    continue
                if relative.suffix.lower() not in extensions:
                    continue
                if relative.is_absolute() or ".." in relative.parts or "\\" in entry.filename:
                    raise ValueError("Unsafe archive path: " + entry.filename)
                if entry.filename.casefold() in names:
                    raise ValueError("Duplicate archive photo: " + entry.filename)
                names.add(entry.filename.casefold())
                target = staging.joinpath(*relative.parts)
                target.parent.mkdir(parents=True, exist_ok=True)
                # Reading each member fully verifies its ZIP CRC as well.
                with archive.open(entry) as source, target.open("xb") as output:
                    shutil.copyfileobj(source, output)
                if target.stat().st_size != entry.file_size:
                    raise ValueError("Incomplete photo: " + entry.filename)
                photos.append({"file": relative.as_posix(), "bytes": entry.file_size, "sha256": digest(target)})
        if len(photos) < 3:
            raise ValueError("The export does not contain a photogrammetry source image set.")
        manifest = {
            "polycamCaptureId": capture_id,
            "archiveSha256": digest(archive_path),
            "photos": sorted(photos, key=lambda photo: photo["file"]),
        }
        staging.rename(destination)
    inventory.write_text(json.dumps(manifest, indent=2) + "\n")
    print(json.dumps({"directory": str(directory), "photos": len(photos), "photoManifestSha256": digest(inventory)}))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("archive", type=pathlib.Path)
    parser.add_argument("dataset_directory", type=pathlib.Path)
    parser.add_argument("capture_id")
    arguments = parser.parse_args()
    import_photos(arguments.archive, arguments.dataset_directory, arguments.capture_id)
