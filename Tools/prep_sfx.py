#!/usr/bin/env python3
"""Cut Maze Swarm's combat sounds from Manuel's recorded sound library (standard library + macOS afconvert).

    python3 Tools/prep_sfx.py        -> Assets/Maze/Resources/Audio/*.wav (44.1 kHz, 16-bit, mono)

The library under /Volumes/T7 Shield/Assets/Sounds is only read, never modified: each source is first
converted by afconvert to a 44.1 kHz mono temp copy. Per clip: cut at the shot / impact onset (short
pre-roll), fixed length (or a fixed region for the reload clicks), 1 ms fade-in, cosine fade-out, peak-normalize to -1 dBFS. The "mid distance"
gun takes are used on purpose: the "near" takes are 80-90% sub-150 Hz blast that a phone speaker
cannot play. The goo_* layer comes from a CC0 squish pack in Sounds/squish (MP3, decoded by afconvert).
Sources and licenses are listed in Sounds/CREDITS.txt. (Tools/synth_sfx.py made the
earlier synthesized set.)
"""
import array
import math
import os
import subprocess
import tempfile
import wave

SR = 44100
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "Maze", "Resources", "Audio")
RW = "/Volumes/T7 Shield/Assets/Sounds/Realistic Weapons"
FF = "/Volumes/T7 Shield/Assets/Sounds/Forge & Fantasy - Weapon SFX Pack/WAV/IMPACTS/Juicy"
OGA = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Sounds", "squish")  # CC0 MP3s, see CREDITS
SOURCES = {
    "C_31P": RW + "/AK-47/C_31P.wav", "C_28P": RW + "/AK-47/C_28P.wav", "P_18P": RW + "/PPSh/P_18P.wav", "G_31P": RW + "/Carl Gustav M45/G_31P.wav",
    "O_17P": RW + "/Nova/O_17P.wav", "K_17P": RW + "/Model 12/K_17P.wav", "K_22P": RW + "/Model 12/K_22P.wav",
    **{f"Impact_Juicy_{i:02d}": f"{FF}/Impact_Juicy_{i:02d}.wav" for i in (5, 7, 9, 10)},
    **{f"Arrow_Impact_Flesh_{i:02d}": f"{FF}/Arrow_Impact_Flesh_{i:02d}.wav" for i in (2, 5, 6, 7, 8, 9)},
    "Impact_Body_07": FF.replace("/Juicy", "/Body") + "/Impact_Body_07.wav",
    **{n: f"{OGA}/{n}.mp3" for n in ("squish_01_0", "squish_02", "squish_03", "squish_04", "squish_05", "squish_06", "squishpop")},
}

# gunshots: (clip, source, shot time in the source s, length s, fade-out s)
SHOTS = [
    ("rifle_1", "C_31P", 0.33, 0.45, 0.22), ("rifle_2", "C_31P", 4.40, 0.45, 0.22), ("rifle_3", "C_28P", 0.61, 0.45, 0.22),
    ("smg_1", "G_31P", 0.31, 0.15, 0.07), ("smg_2", "G_31P", 3.50, 0.15, 0.07), ("smg_3", "G_31P", 6.72, 0.15, 0.07),
    ("shotgun_1", "O_17P", 0.68, 0.60, 0.30), ("shotgun_2", "O_17P", 3.70, 0.60, 0.30), ("shotgun_3", "K_17P", 0.91, 0.60, 0.30),
]
# pump: the pull-back and push-forward clicks of a Model 12 rack, spliced closer together (the real gap
# is 0.5-0.8 s, longer than the shotgun's 0.67 s fire period): (clip, source, back a, b, forward a, b, gap)
PUMPS = [("shotgun_pump_1", "K_22P", 4.19, 4.35, 4.69, 4.88, 0.03), ("shotgun_pump_2", "K_17P", 3.065, 3.21, 3.885, 4.05, 0.05)]
# reload handling: quiet mechanical clicks between the shots of the recordings: (clip, source, from s, to s)
CLICKS = [
    ("dry_click", "P_18P", 29.712, 29.79),   # PPSh: light single click
    ("mag_out", "P_18P", 27.622, 27.80),     # PPSh: click + clack
    ("mag_in", "P_18P", 9.412, 9.50),        # PPSh: seat + latch double click
    ("shell_in", "K_22P", 3.272, 3.40),      # Winchester Model 12: double click into the tube
]
# wet one-shots: (clip, source, length s, fade-out s, pitch ratio)
WET = [
    ("squish_1", "Impact_Juicy_05", 0.45, 0.2, 1.0), ("squish_2", "Impact_Juicy_07", 0.45, 0.2, 1.0),
    ("squish_3", "Impact_Juicy_09", 0.45, 0.2, 1.0), ("squish_4", "Impact_Juicy_10", 0.45, 0.2, 1.0),
    ("hit_1", "Arrow_Impact_Flesh_02", 0.22, 0.1, 1.0), ("hit_2", "Arrow_Impact_Flesh_05", 0.22, 0.1, 1.0),
    ("hit_3", "Arrow_Impact_Flesh_06", 0.22, 0.1, 1.0),
    ("drip_1", "Arrow_Impact_Flesh_07", 0.14, 0.07, 1.3), ("drip_2", "Arrow_Impact_Flesh_08", 0.14, 0.07, 1.3),
    ("drip_3", "Arrow_Impact_Flesh_09", 0.14, 0.07, 1.3),
    # goo_*: the second layer of a Drone death, played with one squish_* (opengameart squish pack)
    ("goo_1", "squish_01_0", 0.32, 0.12, 1.0), ("goo_2", "squish_02", 0.32, 0.12, 1.0),
    ("goo_3", "squish_03", 0.32, 0.12, 1.0), ("goo_4", "squish_04", 0.32, 0.12, 1.0),
    ("goo_5", "squish_05", 0.32, 0.12, 1.0), ("goo_6", "squish_06", 0.32, 0.12, 1.0),
    ("goo_7", "squishpop", 0.25, 0.1, 1.0),
    # the Bloater: a deep juicy burst (death), a stretched wet squelch (spawning minis), a low wet thud (footfalls)
    ("bloater_burst", "Impact_Juicy_05", 0.75, 0.3, 0.6),
    ("bloater_spawn", "squish_06", 0.55, 0.2, 0.72),
    ("bloater_step", "Impact_Body_07", 0.3, 0.12, 0.62),
]


# ---------------------------------------------------------------- io
def load(path):
    w = wave.open(path)
    assert w.getframerate() == SR and w.getnchannels() == 1 and w.getsampwidth() == 2
    return [v / 32768 for v in array.array('h', w.readframes(w.getnframes()))]


def write(path, y):
    w = wave.open(path, 'wb')
    w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
    w.writeframes(array.array('h', [max(-32767, min(32767, int(round(v * 32767)))) for v in y]).tobytes())
    w.close()


# ---------------------------------------------------------------- processing
def onset(x, t, look=0.03, thr=0.1):
    """first sample above thr x the local peak, searching from t - look to t + 60 ms"""
    a = max(0, int((t - look) * SR)); b = int((t + 0.06) * SR)
    pk = max(abs(v) for v in x[a:b])
    return next(i for i in range(a, b) if abs(x[i]) > thr * pk)


def pitch_up(x, ratio):
    """linear-interpolation resample: ratio > 1 higher and shorter, < 1 deeper and longer"""
    out = []
    for i in range(int((len(x) - 1) / ratio)):
        j = i * ratio; a = int(j)
        out.append(x[a] + (x[a + 1] - x[a]) * (j - a))
    return out


def finish(y, fade, peak_db=-1.0):
    fi = int(0.001 * SR); fo = int(fade * SR)
    for i in range(min(fi, len(y))): y[i] *= i / fi
    for i in range(min(fo, len(y))): y[len(y) - 1 - i] *= 0.5 - 0.5 * math.cos(math.pi * i / fo)
    g = 10 ** (peak_db / 20) / (max(abs(v) for v in y) or 1)
    return [v * g for v in y]


def cut(x, t, length, fade, pre=0.002, pitch=1.0, thr=0.1):
    a = max(0, onset(x, t, thr=thr) - int(pre * SR))
    y = x[a:a + int((length * pitch + 0.05) * SR)]
    if pitch != 1.0: y = pitch_up(y, pitch)
    return finish(y[:int(length * SR)], fade)


def splice(x, a1, b1, a2, b2, gap):
    p1 = x[int(a1 * SR):int(b1 * SR)]; p2 = x[int(a2 * SR):int(b2 * SR)]
    n = int(0.004 * SR)
    for i in range(n): p1[-1 - i] *= i / n; p2[i] *= i / n
    return finish(p1 + [0.0] * int(gap * SR) + p2, 0.03)


def first_sound(x, thr_db=-30):
    g = max(abs(v) for v in x) * 10 ** (thr_db / 20)
    return next(i for i, v in enumerate(x) if abs(v) > g) / SR


def main():
    with tempfile.TemporaryDirectory() as tmp:
        src = {}
        for key, path in SOURCES.items():
            conv = os.path.join(tmp, key + ".wav")
            subprocess.run(["afconvert", "-f", "WAVE", "-d", "LEI16@44100", "-c", "1", path, conv], check=True)
            src[key] = load(conv)
        clips = {}
        for name, s, t, length, fade in SHOTS:
            clips[name] = cut(src[s], t, length, fade)
        for name, s, a1, b1, a2, b2, gap in PUMPS:
            clips[name] = splice(src[s], a1, b1, a2, b2, gap)
        for name, s, a, b in CLICKS:
            clips[name] = finish(src[s][int(a * SR):int(b * SR)], 0.02)
        for name, s, length, fade, pitch in WET:
            x = src[s]
            clips[name] = cut(x, first_sound(x) + 0.03, length, fade, pre=0.001, pitch=pitch, thr=0.03)
    os.makedirs(OUT, exist_ok=True)
    for name, y in clips.items():
        write(os.path.join(OUT, name + ".wav"), y)
        print(f"{name:16s} {len(y) / SR * 1000:4.0f} ms")


if __name__ == "__main__":
    main()
