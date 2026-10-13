# Repeatable opaque-rendering benchmark

## Run from the editor

Open CoD in the updated PlutoGE editor, then choose **File → Project Benchmark →
Run Render Benchmark**. The editor uses its matching native runtime and runs the
three default normal/scalar pairs. Stop Play mode and finish any bake, import or
script build first. The action uses the saved benchmark profile and saved scene;
unsaved viewport movement and scene edits are not captured.

Editor rendering and authoring polls pause while the external benchmark runs,
keeping editor GPU work out of the measurements. Native window messages continue
to be processed. Rendering resumes when the benchmark exits; completion, failure
details and the results directory appear in **View → Console**. Closing the editor
is deferred until the capture finishes. The benchmark processes retain their
configured timeouts.

The same submenu provides **Open Results Folder** and **Open Benchmark Profile**.
Each run gets its own `BenchmarkResults/editor-...` directory, with `launcher.log`
in addition to the ordinary capture files. Python 3.11 or newer must be available
as `python` on PATH. A missing interpreter or outdated runtime is reported in
Console. Restart the editor after rebuilding it to see the new menu.

## Run from the command line

From the CoD project directory:

```powershell
python Tools/benchmark_render.py run
```

This runs three normal/scalar pairs in separate native processes, alternating
order between repeats. The default runtime is the local engine's
`out/build/msvc-nvidia/runtime/RelWithDebInfo/PlutoGERuntime.exe`.
Override it with `--runtime <exe>` when needed. Rebuild it after engine changes:

```powershell
cmake --build C:/Users/Cameron.Airlie/dev/PlutoGE/out/build/msvc-nvidia --config RelWithDebInfo --target PlutoGERuntime -j 8
```

`opaque.json` selects Main, anchors player entity 1 at a fixed authored transform,
and sets a 1138 × 634 native-resolution viewport, 240 warmup frames and 600
measured frames. Make another JSON profile for another viewpoint or resolution
and pass `--profile <file>`. The child camera's local transform, FOV, materials,
post-processing, shadow settings and other cameras come from the saved scene.
They are included in the input fingerprint. The player's transform is overridden
only in a disposable scene snapshot.

The profile also explicitly sets directional light entity 6 to conventional
cascaded shadows, one 2048-pixel cascade and softness 2, matching the filtering
configuration being investigated. The launcher verifies the runtime's active light,
shadow method and diagnostic variant. Frozen shadows may be cached after warmup,
so their raster scope can be absent while opaque surfaces still sample the maps.
These light properties are overridden only in the disposable snapshot;
the saved scene can continue using virtual shadows.

Gameplay is never started: scripts, runtime bot spawning and physics simulation
are absent; component updates receive zero delta, keeping authored animation
poses and shader time fixed. Temporal rendering histories still warm up normally.
Authored geometry and enabled secondary cameras are retained. This is a renderer
benchmark, not a whole-game FPS test or a recreation of the nine live bots in an
editor capture. Save a dedicated scene with the desired actors for that workload.

The scalar variant changes conventional directional-shadow filtering in the base
camera only. Secondary views use the same production path in both variants.
Both variants render the same shadow maps; neither lowers shadow quality.

Results go into ignored `BenchmarkResults/<UTC timestamp>/`:

- A scene snapshot, manifest copy, conditions and runtime/shader provenance.
- `normal-1.csv`, `scalar-1.csv`, etc.: frame metrics and actual resolutions,
  geometry counts and GPU observation IDs.
- `.gpu.csv`: scope statistics. Repeated scope names receive occurrence numbers
  rather than being pooled across cameras. Parent scopes include children.
- `.json`: mean/p95 summaries and observed workload counters.
- `comparison-1.json`, etc.: scalar-to-normal frame and individual GPU-scope
  comparisons for each pair. Negative percentages mean normal is faster.
- `.log`: process output. `COMPLETE` appears only when every process exits
  successfully, all captures validate and project input hashes remain unchanged.

GPU observations are asynchronous. Only new, nonzero observation IDs contribute
to GPU summaries; CPU and GPU rows are not synchronized frame pairs.

Compare two individual reports, including reports from different engine builds:

```powershell
python Tools/benchmark_render.py compare BenchmarkResults/<run>/scalar-1.json BenchmarkResults/<run>/normal-1.json
```

Negative percentages mean the second report is faster. Comparisons reject
different profiles, asset/import-artifact contents, project settings, machine/OS/
GPU-driver inventory, resolutions or observed draw counts. Runtime and shader
hashes are recorded as provenance and may differ for before/after engine tests.
Use the same profile and saved assets for both builds. Keep background GPU work,
power settings and temperature comparable; a tool cannot enforce these externally.

The disposable manifest lives temporarily in the project root to preserve Library
asset routes and is removed on exit. The scene, prefabs, original project manifest
and player preferences are not edited. The archived manifest is for inspection;
rerun the launcher rather than launching that copy from its results directory.

An exclusive `.benchmark.lock` prevents overlapping launcher suites. If the launcher
is forcibly killed, check that its recorded PID has exited before deleting the stale
lock. Increase `--timeout` from its default 300 seconds
if loading is slow. A failed or timed-out process leaves logs but no successful
suite marker. Run `python -m unittest discover -s Tools/Benchmarks -p test_*.py`
for launcher regression tests.

## Cloud optimization validation

The next renderer pass reuses volume-space primary/light rays and light-ray
reciprocals, and stops evaluating noise octaves only when their bounded remaining
contribution cannot produce nonzero density. Quality settings and sample counts
remain unchanged. A compile-time test shader retains the original calculations;
production shaders have no runtime reference switch.

The controlled reference–optimized–optimized–reference sequence in
`BenchmarkResults/cloud-density-pass-20261013T104147Z/summary.json` measured 1,200
frames per variant, with matching profile, input hashes and workload counters:

| GPU scope | Reference mean | Optimized mean | Reduction |
| --- | ---: | ---: | ---: |
| Volumetric clouds | 3.284 ms | 3.013 ms | 8.2% |
| Main post-processing | 11.097 ms | 10.814 ms | 2.6% |
| Scene GPU | 13.091 ms | 12.805 ms | 2.2% |

Parent scopes include children; these savings must not be added together.
These numbers apply to the saved frozen benchmark, rather than live gameplay.
Wall-clock CPU frame times were noisier than the isolated cloud GPU timings.

Both graphics backends passed 24 reference image comparisons with zero RGBA8
pixel differences in the tested cases. The tests cover sparse/dense coverage,
rotated and nonuniform volumes, grazing/parallel light rays, full/half-resolution
tracing and sample-count limits. Run the regression checks from the engine root:

```powershell
ctest --test-dir out/build/msvc-nvidia -C RelWithDebInfo -R CloudTransform --output-on-failure
```

## Structural shadow submission pass

The latest editor capture reports 649 shadow draws, about 3.95 ms of CPU shadow
scope time and 1.92 ms across the two GPU CSM scopes. Scope timings overlap and
must not be added to frame time. The enabled renderer change groups opaque
casters by geometry after cascade culling, across material boundaries, into
64-instance batches. A position-only shadow pipeline removes material uploads,
texture bindings and graph evaluation for eligible casters. Masks and vertex
deformation retain the general pipeline; shadow resolution and filtering are
unchanged. Existing unchanged-cascade caching remains active.

A separate stationary/moving shadow-layer implementation is **disabled by
default**. At 2048px its full-map depth seed regressed the Vulkan fixture GPU
time (0.362 to 0.572 ms), despite reducing rerendering to nine moving casters.
It is available only through `SetStaticShadowCachingEnabled` for further
controlled renderer experiments, with a 64 MiB cache limit per renderer.

Run the native image regressions and ABBA timing fixture from the engine root:

```powershell
ctest --test-dir out/build/msvc-nvidia -C RelWithDebInfo -R OpaqueShadowBatching --output-on-failure
```

The fixture renders 649 casters (332,288 triangles) into a 2048px cascade, with
nine movers invalidating it every frame. Batching preserves all 649 casters and
reduces 649 draws/uploads to 11. Correctness checks also cover masked and
deformed casters, mutable instance transforms, mirrored geometry, four cascades,
culling and cached stationary depth. Logs are retained in
`BenchmarkResults/shadow-structural-pass-20261013/`.

Final ABBA measurements (160 measured CPU frames per variant):

| Backend / scope | Legacy | Batched | Reduction |
| --- | ---: | ---: | ---: |
| Vulkan shadow recording CPU | 0.503 ms | 0.172 ms | 65.8% |
| Vulkan cascade GPU | 0.363 ms | 0.356 ms | 1.8% |
| OpenGL shadow recording CPU | 2.787 ms | 0.156 ms | 94.4% |

OpenGL's RHI does not expose GPU scope timings. Both backend regression runs
passed, as did rebuilding the native editor/runtime and loading the saved CoD
scene through a complete normal benchmark run. These measurements establish a
submission optimization; the GPU improvement is small in this fixture.

The frozen project benchmark normally hits the unchanged shadow cache and
cannot establish this pass's dynamic-scene gain. Use a fresh editor capture with
matching camera and moving actors to measure the actual CoD result. The main
opaque GPU cost (6.39 ms in the latest capture) remains a separate target;
fixture submission savings are not a measured game FPS improvement.
