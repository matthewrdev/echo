#!/usr/bin/env python3
"""Create complete Echo projects and a reference comparison plan from local fixtures.

Run check-reference-assets.sh first. Reconstruction is optional and uses the
current CLI with explicitly supplied settings; existing projects are not replaced.
"""

import argparse
import json
import pathlib
import subprocess


def prepare(arguments):
    repository = pathlib.Path(__file__).resolve().parent.parent
    catalogue = repository / "tests/Echo.Tests/Fixtures/reference-datasets.json"
    datasets = json.loads(catalogue.read_text())
    cli = repository / "scripts/echo-photo"
    output = arguments.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    plan = []
    for dataset in datasets:
        name = dataset["folder"].replace("/", "-")
        directory = arguments.assets_root.resolve() / dataset["folder"]
        project = output / (name + ".echo")
        originals = json.loads((directory / "source-photos.json").read_text())["photos"]
        expected = {photo["sha256"] for photo in originals}
        if not project.exists():
            subprocess.run([str(cli), "project", "init", str(project), "--name", dataset["name"]], check=True)
            subprocess.run([str(cli), "--project", str(project), "images", "import", str(directory / "photos"), "--set", dataset["folder"]], check=True)
        data = json.loads(subprocess.check_output([str(cli), "--project", str(project), "project", "info"]))
        if {photo["hash"] for photo in data["images"]} != expected:
            raise ValueError(dataset["name"] + ": project does not contain exactly the distinct source photos; refusing to reuse it")
        if arguments.settings:
            subprocess.run([str(cli), "--project", str(project), "config", "import", str(arguments.settings.resolve())], check=True)
        plan.append({
            "name": name,
            "label": dataset["name"],
            "areaId": dataset["areaId"],
            "polycamCaptureId": dataset["polycamCaptureId"],
            "photosDirectory": str(directory / "photos"),
            "reference": str(directory / dataset["modelFile"]),
            "expectedBytes": dataset["modelBytes"],
            "expectedSha256": dataset["modelSha256"],
            "project": str(project),
            "photos": len(originals),
            "importedDistinctPhotos": len(data["images"]),
        })
        if dataset.get("referenceSceneFile"):
            plan[-1]["referenceScene"] = str(directory / dataset["referenceSceneFile"])
            plan[-1]["referenceSceneSha256"] = dataset["referenceSceneSha256"]
        (output / "reference-validation-plan.json").write_text(json.dumps(plan, indent=2) + "\n")
        if arguments.run_stage:
            with (output / (name + ".ndjson")).open("w") as log:
                subprocess.run([str(cli), "--project", str(project), "run", "start", "--target", arguments.run_stage, "--events", "ndjson"], stdout=log, check=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("assets_root", type=pathlib.Path)
    parser.add_argument("output", type=pathlib.Path)
    parser.add_argument("--settings", type=pathlib.Path)
    parser.add_argument("--run-stage", choices=["alignment", "depth", "cloud", "fusion", "mesh", "texture", "export"])
    prepare(parser.parse_args())
