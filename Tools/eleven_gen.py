#!/usr/bin/env python3
"""Generate Maze Swarm sound takes with ElevenLabs from a batch JSON.

Usage:  python3 eleven_gen.py <batch.json> [--dry-run] [--only id1,id2]
Reads the API key at run time from the macOS keychain (service "elevenlabs-api").
The key is never printed, logged or put on a command line (curl reads it from stdin).
Writes <out_dir>/<id>_take<N>.mp3 and skips takes that already exist, so a rerun
only fills gaps. Nothing plays out loud.
"""
import json, os, subprocess, sys

API = "https://api.elevenlabs.io"

def key():
    k = subprocess.run(["security", "find-generic-password", "-s", "elevenlabs-api", "-w"],
                       capture_output=True, text=True).stdout.strip()
    if not k:
        sys.exit("No key in the keychain under 'elevenlabs-api'.")
    return k

def post(path, body, out, k):
    # Header goes through a curl config on stdin, so the key never appears in `ps`.
    cfg = f'header = "xi-api-key: {k}"\nheader = "Content-Type: application/json"\n'
    r = subprocess.run(["curl", "-sS", "-K", "-", "-X", "POST", API + path,
                        "--data-binary", json.dumps(body), "-o", out, "-w", "%{http_code}"],
                       input=cfg, capture_output=True, text=True)
    code = r.stdout.strip()
    if code != "200":
        err = open(out, "rb").read(400).decode("utf-8", "replace") if os.path.exists(out) else r.stderr
        if os.path.exists(out): os.remove(out)
        print(f"  FAILED ({code}): {err}")
        return False
    return True

def main():
    args = sys.argv[1:]
    if not args: sys.exit(__doc__)
    batch = json.load(open(args[0]))
    dry = "--dry-run" in args
    only = set(args[args.index("--only") + 1].split(",")) if "--only" in args else None
    out_dir = os.path.expanduser(batch["out_dir"])
    os.makedirs(out_dir, exist_ok=True)
    cues = [c for c in batch["cues"] if not only or c["id"] in only]
    sfx_s = sum(max(0.5, c["seconds"]) * c.get("takes", 3) for c in cues if c.get("kind") != "music")
    mus_s = sum(c["seconds"] * c.get("takes", 1) for c in cues if c.get("kind") == "music")
    print(f"{len(cues)} cues -> {out_dir}\n  sound effects: {sfx_s:.1f} s total   music: {mus_s:.0f} s total")
    if dry:
        for c in cues: print(f"  {c['id']}: {c.get('takes', 1 if c.get('kind') == 'music' else 3)} x {c['seconds']} s")
        return
    k = key()
    for c in cues:
        takes = c.get("takes", 1 if c.get("kind") == "music" else 3)
        for t in range(1, takes + 1):
            out = os.path.join(out_dir, f"{c['id']}_take{t}.mp3")
            if os.path.exists(out): continue
            print(f"{c['id']} take {t}")
            if c.get("kind") == "music":
                post("/v1/music", {"prompt": c["prompt"], "music_length_ms": int(c["seconds"] * 1000)}, out, k)
            else:
                body = {"text": c["prompt"], "duration_seconds": max(0.5, min(30.0, c["seconds"])),
                        "prompt_influence": c.get("influence", 0.5)}
                if c.get("loop"): body["loop"] = True
                post("/v1/sound-generation", body, out, k)

if __name__ == "__main__":
    main()
