# Meshy image-to-3D for the crate / door props, one job at a time. The API key comes from the macOS keychain item
# "meshy-api" (fallback ~/.meshy_key), never printed or logged. Downloads each GLB next to its image in <dir>/meshy/<name>.glb and keeps a small json.
#   python3 -I meshy_props.py <image.png> [<image.png> ...]
import base64, json, os, sys, time, urllib.request
API = "https://api.meshy.ai/openapi/v1"
def _key():   # the macOS keychain item "meshy-api" (or ~/.meshy_key); never printed or written anywhere
    import subprocess
    r = subprocess.run(["security", "find-generic-password", "-s", "meshy-api", "-w"], capture_output=True, text=True)
    if r.returncode == 0 and r.stdout.strip(): return r.stdout.strip()
    return open(os.path.expanduser("~/.meshy_key")).read().strip()
KEY = _key()

def call(method, path, body=None):
    req = urllib.request.Request(API + path, method=method, data=json.dumps(body).encode() if body else None,
                                 headers={"Authorization": "Bearer " + KEY, "Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=60) as r: return json.loads(r.read().decode())

def balance():
    try: return call("GET", "/balance").get("balance")
    except Exception as e: return f"? ({type(e).__name__})"

start = balance(); print("balance before:", start, flush=True)
for img in sys.argv[1:]:
    name = os.path.splitext(os.path.basename(img))[0]
    out_dir = os.path.join(os.path.dirname(img), "meshy"); os.makedirs(out_dir, exist_ok=True)
    glb = os.path.join(out_dir, name + ".glb")
    if os.path.exists(glb): print(name, "already done"); continue
    uri = "data:image/png;base64," + base64.b64encode(open(img, "rb").read()).decode()
    try:
        task = call("POST", "/image-to-3d", {"image_url": uri, "should_texture": True, "enable_pbr": True, "should_remesh": True,
                                             "topology": "triangle", "target_polycount": 30000, "symmetry_mode": "auto"})
    except urllib.error.HTTPError as e:
        print(name, "submit failed", e.code, e.read().decode()[:300]); continue
    tid = task.get("result"); print(name, "task", tid, flush=True)
    t0 = time.time()
    while True:
        time.sleep(15)
        st = call("GET", f"/image-to-3d/{tid}")
        s = st.get("status")
        if s in ("SUCCEEDED", "FAILED", "CANCELED", "EXPIRED"): break
        if time.time() - t0 > 1200: print(name, "timed out waiting"); break
    if s != "SUCCEEDED": print(name, "ended", s, (st.get("task_error") or {}).get("message", "")); continue
    url = st["model_urls"]["glb"]
    urllib.request.urlretrieve(url, glb)
    json.dump({k: st.get(k) for k in ("id", "status", "created_at", "finished_at", "texture_urls", "thumbnail_url")},
              open(os.path.join(out_dir, name + ".json"), "w"), indent=1)
    print(name, "saved", glb, os.path.getsize(glb), "bytes", f"{time.time() - t0:.0f}s", flush=True)
end = balance(); print("balance after:", end, flush=True)
if isinstance(start, (int, float)) and isinstance(end, (int, float)): print("credits spent:", start - end)
