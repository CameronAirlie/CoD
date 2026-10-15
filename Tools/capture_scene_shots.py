"""Render review screenshots of a scene with the real runtime (no gameplay scripts).

Usage: python Tools/capture_scene_shots.py <Scene> <shots.json> <outdir> [runtime.exe] [--headless]
shots.json: [{"name": "street", "pos": [x, y, z], "target": [x, y, z], "fov": 70}, ...]
Each shot strips the player rig and HUD, adds a free camera, launches the runtime windowed
and captures the window client area from the screen (Windows only; needs an unlocked desktop).
--headless renders with PlutoGERenderSceneShot instead (works while locked; no particles or UI).
"""
from pathlib import Path
import ctypes, ctypes.wintypes as wt, json, math, os, re, subprocess, sys, time
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_RUNTIME = Path('C:/Users/Cameron.Airlie/dev/PlutoGE/out/build/msvc18/runtime/Release/PlutoGERuntime.exe')
LAB = Path('C:/Users/Cameron.Airlie/dev/PlutoGE/out/build/msvc18/tests/Release/PlutoGERenderSceneShot.exe')
WIDTH, HEIGHT = 1280, 720


def strip_scene(text):
    blocks = re.split(r'(?=^ENTITY\t)', text, flags=re.M)
    head, blocks = blocks[0], blocks[1:]
    parents = {int(b.split('\t')[1]): int(b.split('\t')[2]) for b in blocks}
    drop = {1, 1784, 1196}

    def dropped(i):
        return i in drop or (parents.get(i, 0) != 0 and dropped(parents[i]))
    kept = [b for b in blocks if not dropped(int(b.split('\t')[1]))]
    # Gameplay scripts are not compiled into this capture; remove them from remaining entities.
    kept = [re.sub(r'^COMPONENT\t\d+\tScriptComponent\t1\n.*?^END_COMPONENT\n', '', b, flags=re.M | re.S) for b in kept]
    return head + ''.join(kept)


def camera(pos, target, fov):
    dx, dy, dz = (target[k] - pos[k] for k in range(3))
    yaw = math.degrees(math.atan2(-dx, -dz))
    pitch = math.degrees(math.atan2(dy, math.hypot(dx, dz)))
    v = lambda a: ','.join(f'{x:.5f}' for x in a)
    return (f'ENTITY\t999990\t0\t1\tCAMERA - Review\t{v(pos)}\t{v((pitch, yaw, 0))}\t1,1,1\n'
            'COMPONENT\t999990\tCameraComponent\t1\n'
            f'PROPERTY\tFOV\t0\t{fov}\t0\n'
            'PROPERTY\tProjection\t6\t0\t2\tPerspective\tOrthographic\n'
            'PROPERTY\tNearPlane\t0\t0.100000\t0\nPROPERTY\tFarPlane\t0\t1500.000000\t0\n'
            'PROPERTY\tMainCamera\t4\ttrue\t0\n'
            'PROPERTY\tPostProcessPresetAsset\t2\tproject://PostProcessing/main.plutopostprocess\t0\n'
            'END_COMPONENT\n'
            'COMPONENT\t999990\tSoundListenerComponent\t1\nPROPERTY\tPrimary\t4\ttrue\t0\nEND_COMPONENT\n')


user32 = ctypes.windll.user32 if os.name == 'nt' else None
if user32:
    user32.SetProcessDPIAware()


def find_window(pid):
    found = []
    proc = ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)

    def cb(hwnd, _):
        owner = wt.DWORD()
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(owner))
        if owner.value == pid and user32.IsWindowVisible(hwnd):
            found.append(hwnd)
        return True
    user32.EnumWindows(proc(cb), 0)
    return found[0] if found else None


def grab(hwnd):
    # Vulkan swapchains do not respond to PrintWindow; capture the composited screen instead.
    from PIL import ImageGrab
    user32.ShowWindow(hwnd, 9)
    user32.SetForegroundWindow(hwnd)
    time.sleep(1.0)
    rect = wt.RECT()
    user32.GetClientRect(hwnd, ctypes.byref(rect))
    origin = wt.POINT(0, 0)
    user32.ClientToScreen(hwnd, ctypes.byref(origin))
    return ImageGrab.grab((origin.x, origin.y, origin.x + rect.right, origin.y + rect.bottom), all_screens=True)


def main():
    headless = '--headless' in sys.argv
    args = [a for a in sys.argv[1:] if a != '--headless']
    scene_name, shots_path, outdir = args[0], Path(args[1]), Path(args[2])
    runtime = Path(args[3]) if len(args) > 3 else DEFAULT_RUNTIME
    settle = float(os.environ.get('SHOT_SETTLE', '14'))
    outdir.mkdir(parents=True, exist_ok=True)
    work = ROOT / 'Tools/Native/Shots'; work.mkdir(parents=True, exist_ok=True)
    base = strip_scene((ROOT / f'Assets/Scenes/{scene_name}.plutoscene').read_text())
    shots = json.loads(shots_path.read_text())
    source = [l for l in (ROOT / 'CoDplutoproject.plutoproject').read_text().splitlines()
              if l.split('\t')[0] not in ('ASSET_DIR', 'STARTUP_SCENE', 'WINDOW_SIZE', 'VSYNC', 'GRAPHICS_API', 'FULLSCREEN', 'WINDOW_MODE')]
    for shot in shots:
        scene_path = work / f'{scene_name}-{shot["name"]}.plutoscene'
        scene_path.write_text(base + camera(shot['pos'], shot['target'], shot.get('fov', 70)))
        if headless:
            ppm = work / f'{shot["name"]}.ppm'
            result = subprocess.run([str(LAB), str(ROOT), str(scene_path), str(ppm), 'Review', '--size', f'{WIDTH}x{HEIGHT}'], cwd=LAB.parent, capture_output=True, text=True)
            if result.returncode != 0:
                print('FAILED', shot['name'], result.stdout[-400:], result.stderr[-800:]); continue
            Image.open(ppm).save(outdir / f'{shot["name"]}.png'); ppm.unlink()
            print('captured', outdir / f'{shot["name"]}.png')
            continue
        manifest = ROOT / f'ReviewShot-{shot["name"]}.plutoproject'
        manifest.write_text('\n'.join(source + [f'ASSET_DIR\t{(ROOT / "Assets").as_posix()}', f'STARTUP_SCENE\t{scene_path.as_posix()}',
                                                f'WINDOW_SIZE\t{WIDTH}\t{HEIGHT}', 'VSYNC\t1', 'GRAPHICS_API\tVulkan']) + '\n')
        log = (work / f'{shot["name"]}.log').open('w')
        proc = subprocess.Popen([str(runtime), str(manifest)], cwd=runtime.parent, stdout=log, stderr=subprocess.STDOUT)
        try:
            hwnd = None
            for _ in range(int(settle * 10) + 300):
                time.sleep(.1)
                hwnd = hwnd or find_window(proc.pid)
                if proc.poll() is not None:
                    break
                if hwnd and _ >= settle * 10:
                    break
            if hwnd and proc.poll() is None:
                path = outdir / f'{shot["name"]}.png'
                grab(hwnd).save(path)
                print('captured', path)
            else:
                print('FAILED', shot['name'], 'see', work / f'{shot["name"]}.log')
        finally:
            if proc.poll() is None:
                proc.terminate(); proc.wait(timeout=15)
            log.close(); manifest.unlink(missing_ok=True)


if __name__ == '__main__':
    main()
