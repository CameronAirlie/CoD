"""Repeatable frozen-scene Vulkan benchmarks. Uses only Python's standard library."""
from __future__ import annotations

import argparse
import csv
import hashlib
import json
import math
import os
from pathlib import Path
import platform
import re
import statistics
import subprocess
import sys
import tempfile
from datetime import datetime, timezone

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_RUNTIME = Path("C:/Users/Cameron.Airlie/dev/PlutoGE/out/build/msvc-nvidia/runtime/RelWithDebInfo/PlutoGERuntime.exe")
PROFILE = ROOT / "Tools/Benchmarks/opaque.json"


def digest(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def asset_digest(root: Path) -> str:
    """Hash inputs, excluding managed binaries (gameplay is never started)."""
    result = hashlib.sha256()
    for path in sorted(root.rglob("*")):
        if not path.is_file() or "Managed" in path.relative_to(root).parts:
            continue
        result.update(path.relative_to(root).as_posix().encode() + b"\0")
        result.update(bytes.fromhex(digest(path)))
    return result.hexdigest()


def snapshot_scene(text: str, anchor: dict, overrides: list | tuple = ()) -> str:
    lines = text.splitlines()
    matches = 0
    for i, line in enumerate(lines):
        fields = line.split("\t")
        if fields[:2] == ["ENTITY", str(anchor["entity"])]:
            if len(fields) != 8:
                raise ValueError("Unexpected scene entity format")
            fields[5] = ",".join(map(str, anchor["position"]))
            fields[6] = ",".join(map(str, anchor["rotation"]))
            lines[i] = "\t".join(fields)
            matches += 1
    if matches != 1:
        raise ValueError("Camera anchor must identify exactly one serialized entity")
    for override in overrides:
        component = None
        matches = 0
        for i, line in enumerate(lines):
            fields = line.split("\t")
            if fields[0] == "COMPONENT":
                component = (fields[1], fields[2])
            elif fields[0] == "END_COMPONENT":
                component = None
            elif component == (str(override["entity"]), override["component"]) and fields[:2] == ["PROPERTY", override["property"]]:
                fields[3] = str(override["value"])
                lines[i] = "\t".join(fields)
                matches += 1
        if matches != 1:
            raise ValueError(f"Component override must match exactly one property: {override}")
    return "\n".join(lines) + "\n"


def load_profile(path: Path) -> dict:
    profile = json.loads(path.read_text(encoding="utf-8"))
    if profile.get("version") != 1:
        raise ValueError("Unsupported benchmark profile version")
    for key, low, high in [("width", 64, 8192), ("height", 64, 8192), ("frames", 1, 100000), ("warmup", 1, 100000)]:
        value = profile[key]
        if type(value) is not int or not low <= value <= high:
            raise ValueError(f"Invalid profile {key}")
    anchor = profile["camera_anchor"]
    for key in ["position", "rotation"]:
        if len(anchor[key]) != 3 or any(not math.isfinite(float(v)) for v in anchor[key]):
            raise ValueError(f"Invalid camera {key}")
    scene = (ROOT / profile["scene"]).resolve()
    if not scene.is_relative_to((ROOT / "Assets").resolve()) or not scene.is_file():
        raise ValueError("Profile scene must be an existing project asset")
    return profile


def read_samples(path: Path, expected: int) -> tuple[dict, dict]:
    with path.open(newline="", encoding="utf-8") as stream:
        rows = list(csv.DictReader(stream))
    if len(rows) != expected:
        raise ValueError(f"Incomplete capture: {len(rows)} of {expected} frames")
    required = ["frame_ms", "scene_gpu_ms", "gpu_available", "internal_width", "internal_height",
                "output_width", "output_height", "opaque_triangles", "geometry_draws", "geometry_instances",
                "shadow_draws", "gpu_observation_id"]
    if any(key not in rows[0] for key in required):
        raise ValueError("Runtime lacks frozen-render benchmark metrics; rebuild PlutoGERuntime")
    workload = {key: sorted({float(row[key]) for row in rows}) for key in required[3:-1]}
    summary = {}
    for key in ["frame_ms", "scene_update_ms", "render_present_ms", "scene_gpu_ms", "shadow_recording_ms", "geometry_recording_ms"]:
        values = [float(row[key]) for row in rows if key != "scene_gpu_ms" or row["gpu_available"] == "1"]
        if not values or any(not math.isfinite(v) or v < 0 for v in values):
            raise ValueError(f"Missing or invalid observations for {key}")
        values.sort()
        summary[key] = {"observations": len(values), "mean": statistics.mean(values),
                        "p95": values[math.ceil(.95 * len(values)) - 1]}
    return summary, workload


def compare(left: dict, right: dict) -> dict:
    if left["conditions"] != right["conditions"] or left["workload"] != right["workload"]:
        raise ValueError("Incompatible captures: profile, assets, environment, resolution, or draw workload differs")
    result = {key: {"before_ms": value["mean"], "after_ms": right["metrics"][key]["mean"],
                  "change_percent": (right["metrics"][key]["mean"] / value["mean"] - 1) * 100 if value["mean"] else None}
            for key, value in left["metrics"].items()}
    for name in sorted(left.get("gpu_scopes", {}).keys() & right.get("gpu_scopes", {}).keys()):
        before = float(left["gpu_scopes"][name]["mean_ms"])
        after = float(right["gpu_scopes"][name]["mean_ms"])
        result[f"gpu/{name}"] = {"before_ms": before, "after_ms": after,
                                  "change_percent": (after / before - 1) * 100 if before else None}
    return result


def write_json(path: Path, value: dict) -> None:
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def gpu_inventory() -> str:
    if os.name != "nt":
        return "unavailable"
    result = subprocess.run(["powershell", "-NoProfile", "-NonInteractive", "-Command",
                             "Get-CimInstance Win32_VideoController | Select-Object Name,DriverVersion,PNPDeviceID | ConvertTo-Json -Compress"],
                            capture_output=True, text=True, check=True, timeout=30,
                            creationflags=subprocess.CREATE_NO_WINDOW)
    return result.stdout.strip()


def run(args: argparse.Namespace) -> None:
    # Library routes are relative to the project manifest's parent. Use a
    # disposable manifest in that root rather than silently losing imported assets.
    lock = ROOT / ".benchmark.lock"
    try:
        with lock.open("x", encoding="utf-8") as stream:
            stream.write(str(os.getpid()))
    except FileExistsError:
        raise ValueError("Another benchmark suite is running; inspect .benchmark.lock before removing a stale lock") from None
    try:
        with tempfile.NamedTemporaryFile(dir=ROOT, prefix=".benchmark-", suffix=".plutoproject", delete=False) as temporary:
            manifest = Path(temporary.name)
        try:
            run_with_manifest(args, manifest)
        finally:
            manifest.unlink(missing_ok=True)
    finally:
        lock.unlink(missing_ok=True)


def run_with_manifest(args: argparse.Namespace, manifest: Path) -> None:
    profile = load_profile(args.profile)
    runtime = args.runtime.resolve(strict=True)
    output = args.output.resolve() if args.output else ROOT / "BenchmarkResults" / datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
    output.mkdir(parents=True, exist_ok=False)
    # Only this disposable manifest/scene is modified. Prefab references resolve
    # against the original Assets directory, whose content hash guards comparisons.
    scene_text = snapshot_scene((ROOT / profile["scene"]).read_text(encoding="utf-8"), profile["camera_anchor"],
                                profile.get("component_overrides", []))
    scene = output / "snapshot.plutoscene"
    scene.write_text(scene_text, encoding="utf-8")
    project_manifest = ROOT / "CoDplutoproject.plutoproject"
    overrides = {"NAME": "CoD Render Benchmark", "ASSET_DIR": (ROOT / "Assets").as_posix(),
                 "STARTUP_SCENE": scene.as_posix(), "WINDOW_TITLE": "CoD Render Benchmark",
                 "WINDOW_SIZE": f"{profile['width']}\t{profile['height']}", "VSYNC": "0",
                 "GRAPHICS_API": "Vulkan", "RUNTIME_UPSCALER": "None", "RUNTIME_RENDER_SCALE": "1.0",
                 "SCRIPT_ASSEMBLY": ""}
    source_manifest = project_manifest.read_text(encoding="utf-8")
    manifest_text = "\n".join(line for line in source_manifest.splitlines()
                              if line.split("\t", 1)[0] not in overrides)
    manifest_text += "\n" + "\n".join(f"{key}\t{value}" for key, value in overrides.items()) + "\n"
    manifest.write_text(manifest_text, encoding="utf-8")
    (output / "benchmark.plutoproject").write_text(manifest_text, encoding="utf-8")
    print("Hashing project assets…", flush=True)
    assets = asset_digest(ROOT / "Assets")
    artifacts = asset_digest(ROOT / "Library/Artifacts")
    conditions = {"profile": profile, "assets_sha256": assets,
                  "artifacts_sha256": artifacts, "project_sha256": digest(project_manifest),
                  "snapshot_sha256": digest(scene), "mode": "frozen-render", "vsync": False,
                  "backend": "Vulkan", "upscaler": "None", "render_scale": 1.0,
                  "machine": platform.node(), "os": platform.platform(),
                  "cpu": platform.processor(), "gpu_inventory": gpu_inventory()}
    provenance = {"runtime": str(runtime), "runtime_sha256": digest(runtime),
                  "shader_sha256": asset_digest(runtime.parent / "Shaders"),
                  "utc": datetime.now(timezone.utc).isoformat()}
    write_json(output / "conditions.json", {"conditions": conditions, "provenance": provenance})
    variants = ["normal", "scalar"] if args.variant == "both" else [args.variant]
    reports = {}
    for repeat in range(args.repeats):
        for variant in variants if repeat % 2 == 0 else list(reversed(variants)):
            label = f"{variant}-{repeat + 1}"
            csv_path = output / f"{label}.csv"
            command = [str(runtime), "--benchmark-project", str(manifest), str(csv_path),
                       str(profile["frames"]), str(profile["warmup"]), "--frozen-render"]
            if variant == "scalar":
                command.append("--scalar-shadows")
            print(f"Running {label}…", flush=True)
            with (output / f"{label}.log").open("w", encoding="utf-8") as log:
                subprocess.run(command, cwd=runtime.parent, stdout=log, stderr=subprocess.STDOUT,
                               timeout=args.timeout, check=True,
                               creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0)
            metrics, workload = read_samples(csv_path, profile["frames"])
            if any(len(values) != 1 for values in workload.values()):
                raise ValueError("Frozen workload changed during capture; increase warmup or inspect scene streaming")
            if workload["internal_width"] != [profile["width"]] or workload["internal_height"] != [profile["height"]]:
                raise ValueError("Actual internal resolution does not match profile")
            if workload["output_width"] != [profile["width"]] or workload["output_height"] != [profile["height"]]:
                raise ValueError("Actual output resolution does not match profile")
            if not workload["geometry_draws"] or min(workload["geometry_draws"]) <= 0:
                raise ValueError("Benchmark rendered no geometry")
            gpu_path = csv_path.with_suffix(".gpu.csv")
            with gpu_path.open(newline="", encoding="utf-8") as stream:
                scopes = {row["scope"]: row for row in csv.DictReader(stream)}
            if "RHI Geometry / Opaque and alpha-tested #1" not in scopes:
                raise ValueError("Capture has no opaque GPU scope")
            log_text = (output / f"{label}.log").read_text(encoding="utf-8", errors="replace")
            actual = re.search(r"Benchmark base camera: (.*); lights (\d+); directional intensity ([\d.eE+-]+); shadows ([01]); shadow method (\d+); diagnostic mode (\d+)", log_text)
            if not actual:
                raise ValueError("Runtime did not report benchmark camera conditions")
            if int(actual[6]) != (3 if variant == "scalar" else 0):
                raise ValueError("Runtime did not apply the requested diagnostic variant")
            if profile.get("require_cascaded_shadows") and (actual[4] != "1" or actual[5] != "0" or float(actual[3]) <= 0):
                raise ValueError("Conventional directional shadows are not active")
            workload["camera_position"] = actual[1]
            workload["directional_intensity"] = float(actual[3])
            workload["lights"] = int(actual[2])
            workload["shadow_method"] = int(actual[5])
            report = {"conditions": conditions, "provenance": provenance, "variant": variant,
                      "metrics": metrics, "workload": workload, "gpu_scopes": scopes}
            write_json(output / f"{label}.json", report)
            reports[label] = report
            print(f"  Frame {metrics['frame_ms']['mean']:.3f} ms; scene GPU {metrics['scene_gpu_ms']['mean']:.3f} ms", flush=True)
    if (asset_digest(ROOT / "Assets") != assets or asset_digest(ROOT / "Library/Artifacts") != artifacts
            or digest(project_manifest) != conditions["project_sha256"]
            or digest(runtime) != provenance["runtime_sha256"]
            or asset_digest(runtime.parent / "Shaders") != provenance["shader_sha256"]):
        raise ValueError("Project assets changed during benchmark; results are not comparable")
    if variants == ["normal", "scalar"]:
        for repeat in range(1, args.repeats + 1):
            comparison = compare(reports[f"scalar-{repeat}"], reports[f"normal-{repeat}"])
            write_json(output / f"comparison-{repeat}.json", comparison)
            opaque = comparison["gpu/RHI Geometry / Opaque and alpha-tested #1"]
            print(f"Pair {repeat} opaque GPU: scalar {opaque['before_ms']:.3f} ms, "
                  f"normal {opaque['after_ms']:.3f} ms ({opaque['change_percent']:+.1f}%)")
    (output / "COMPLETE").write_text("All requested processes exited successfully; asset fingerprint unchanged.\n", encoding="utf-8")
    print(f"Results: {output}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    capture = sub.add_parser("run")
    capture.add_argument("--runtime", type=Path, default=DEFAULT_RUNTIME)
    capture.add_argument("--profile", type=Path, default=PROFILE)
    capture.add_argument("--output", type=Path)
    capture.add_argument("--variant", choices=["normal", "scalar", "both"], default="both")
    capture.add_argument("--repeats", type=int, choices=range(1, 11), default=3)
    capture.add_argument("--timeout", type=int, default=300)
    comparison = sub.add_parser("compare")
    comparison.add_argument("before", type=Path)
    comparison.add_argument("after", type=Path)
    args = parser.parse_args()
    try:
        if args.command == "run":
            run(args)
        else:
            for path in [args.before, args.after]:
                if not (path.parent / "COMPLETE").is_file():
                    raise ValueError(f"Capture suite incomplete: {path.parent}")
            print(json.dumps(compare(json.loads(args.before.read_text()), json.loads(args.after.read_text())), indent=2))
        return 0
    except (OSError, ValueError, KeyError, subprocess.SubprocessError) as error:
        print(f"Benchmark failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
