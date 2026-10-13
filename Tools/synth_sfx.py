#!/usr/bin/env python3
"""Offline synthesis of Maze Swarm's combat sounds (no samples, no downloads, standard library only).

SUPERSEDED (Oct 10, 2026): the game now uses recorded clips made by Tools/prep_sfx.py. Running this
would overwrite squish/hit/drip and write the old single-take names; the synth set is kept in Sounds/synth_backup.

    python3 Tools/synth_sfx.py        -> Assets/Maze/Resources/Audio/*.wav (44.1 kHz, 16-bit, mono)

Every gunshot layers four parts, the way recorded shots are built up in a mix:
  transient click (1-3 ms of bright noise), band-passed noise burst (the crack / body),
  a low sine thump with a pitch drop (60-120 Hz, fast decay), and a short filtered reverb tail.
The Drone death is a wet splat (noise through a closing low-pass) over a pitch-dropping, wobbling
body plus a few bubble "gloops"; bullet hits are small wet thwacks; drips are the droplet blips the
game plays as the goo blobs land.
"""
import math
import os
import random
import struct
import wave

SR = 44100
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "Maze", "Resources", "Audio")


# ---------------------------------------------------------------- building blocks
def zeros(sec):
    return [0.0] * int(sec * SR)


def noise(n, rng):
    return [rng.uniform(-1.0, 1.0) for _ in range(n)]


def env_exp(n, tau, attack=0.0005, delay=0.0):
    out = []
    for i in range(n):
        t = i / SR - delay
        if t < 0:
            out.append(0.0)
        elif t < attack:
            out.append(t / attack)
        else:
            out.append(math.exp(-(t - attack) / tau))
    return out


def mul(a, b):
    return [x * y for x, y in zip(a, b)]


def add_into(dst, src, gain=1.0, at=0.0):
    o = int(at * SR)
    for i, v in enumerate(src):
        if 0 <= o + i < len(dst):
            dst[o + i] += v * gain
    return dst


def biquad(x, kind, f0, q=0.707):
    """RBJ cookbook biquad: 'lp', 'hp' or 'bp' (constant 0 dB peak)."""
    w0 = 2 * math.pi * f0 / SR
    c, s = math.cos(w0), math.sin(w0)
    alpha = s / (2 * q)
    if kind == "lp":
        b0, b1, b2 = (1 - c) / 2, 1 - c, (1 - c) / 2
    elif kind == "hp":
        b0, b1, b2 = (1 + c) / 2, -(1 + c), (1 + c) / 2
    else:
        b0, b1, b2 = alpha, 0.0, -alpha
    a0, a1, a2 = 1 + alpha, -2 * c, 1 - alpha
    b0, b1, b2, a1, a2 = b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0
    y, x1, x2, y1, y2 = [], 0.0, 0.0, 0.0, 0.0
    for v in x:
        o = b0 * v + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2
        x2, x1, y2, y1 = x1, v, y1, o
        y.append(o)
    return y


def sweep_lp(x, f_start, f_end, tau):
    """One-pole low-pass whose cutoff glides exponentially from f_start to f_end (time constant tau)."""
    y, z = [], 0.0
    for i, v in enumerate(x):
        f = f_end + (f_start - f_end) * math.exp(-(i / SR) / tau)
        a = 1 - math.exp(-2 * math.pi * f / SR)
        z += (v - z) * a
        y.append(z)
    return y


def tone(n, f0, f1, tau_pitch, wobble=0.0, wobble_hz=0.0, phase=0.0):
    """Sine whose pitch glides from f0 to f1 (exponential, time constant tau_pitch), optional FM wobble."""
    out, ph = [], phase
    for i in range(n):
        t = i / SR
        f = f1 + (f0 - f1) * math.exp(-t / tau_pitch)
        if wobble:
            f *= 1 + wobble * math.sin(2 * math.pi * wobble_hz * t)
        ph += 2 * math.pi * f / SR
        out.append(math.sin(ph))
    return out


def reverb(x, decay, damp_hz=3500.0, length=None):
    """Small Schroeder room: 4 damped feedback combs + 2 all-passes. Returns the wet signal only."""
    n = int((length if length else len(x) / SR + decay) * SR)
    src = x + [0.0] * max(0, n - len(x))
    wet = [0.0] * n
    for ms in (29.7, 37.1, 41.1, 43.7):
        d = int(ms * SR / 1000)
        g = 10 ** (-3 * (ms / 1000) / decay)  # -60 dB after `decay` seconds
        a = 1 - math.exp(-2 * math.pi * damp_hz / SR)
        buf, lp = [0.0] * d, 0.0
        for i in range(n):
            out = buf[i % d]
            lp += (out - lp) * a
            buf[i % d] = src[i] + lp * g
            wet[i] += out * 0.25
    for ms, g in ((5.0, 0.7), (1.7, 0.7)):
        d = int(ms * SR / 1000)
        buf, y = [0.0] * d, []
        for i in range(n):
            bo = buf[i % d]
            v = -g * wet[i] + bo
            buf[i % d] = wet[i] + g * v
            y.append(v)
        wet = y
    return wet


def fade_tail(x, sec=0.03):
    n = int(sec * SR)
    for i in range(min(n, len(x))):
        x[len(x) - 1 - i] *= i / n
    return x


def normalize(x, peak):
    m = max(1e-9, max(abs(v) for v in x))
    return [v * peak / m for v in x]


def soft_clip(x, drive=1.0):
    return [math.tanh(v * drive) / math.tanh(drive) for v in x]


def write(name, x):
    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, name + ".wav")
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(b"".join(struct.pack("<h", int(max(-1.0, min(1.0, v)) * 32767)) for v in x))
    print(f"  {name}.wav  {len(x) / SR:.2f} s")


# ---------------------------------------------------------------- gunshots
def gunshot(seed, length, click_ms, crack_hz, crack_q, crack_tau, body_hz, body_tau, body_gain,
            thump_f0, thump_f1, thump_tau, thump_gain, rumble_tau, rumble_gain, verb_decay, verb_mix, verb_damp, drive, crack_gain=6.0):
    rng = random.Random(seed)
    n = int(length * SR)
    out = zeros(length)
    # 1) transient click: a few ms of high-passed noise
    nc = int(click_ms / 1000 * SR)
    click = biquad(noise(nc, rng), "hp", 2500)
    add_into(out, mul(click, env_exp(nc, click_ms / 3000, attack=0.0001)), 1.6)
    # 2) band-passed noise bursts: the crack and the body of the report
    nz = noise(n, rng)
    add_into(out, mul(biquad(biquad(nz, "bp", crack_hz, crack_q), "bp", crack_hz, crack_q), env_exp(n, crack_tau)), crack_gain)
    add_into(out, mul(biquad(nz, "bp", body_hz, 0.8), env_exp(n, body_tau, attack=0.001)), body_gain)
    # low rumble (muzzle blast pressure)
    if rumble_gain:
        add_into(out, mul(biquad(noise(n, rng), "lp", 260), env_exp(n, rumble_tau, attack=0.002)), rumble_gain)
    # 3) the thump: a low sine with a quick pitch drop
    add_into(out, mul(tone(n, thump_f0, thump_f1, thump_tau * 0.8), env_exp(n, thump_tau, attack=0.0008)), thump_gain)
    dry = soft_clip(out, drive)
    # 4) short filtered reverb tail
    wet = reverb(dry, verb_decay, verb_damp, length)
    mix = [d + w * verb_mix for d, w in zip(dry + [0.0] * (len(wet) - len(dry)), wet)][:n]
    return fade_tail(mix)


def rifle():
    return gunshot(11, 0.62, 2.0, 2600, 0.9, 0.010, 900, 0.028, 1.1, 120, 58, 0.055, 1.35, 0.06, 0.6, 0.34, 0.55, 3200, 1.8, 7.0)


def smg():
    # lighter and drier, so 16 shots per second stays crisp instead of fatiguing
    return gunshot(23, 0.26, 1.2, 3400, 1.1, 0.005, 1500, 0.012, 0.6, 140, 90, 0.022, 0.55, 0.0, 0.0, 0.14, 0.28, 4200, 1.4, 5.5)


def shotgun():
    return gunshot(37, 1.05, 3.0, 1700, 0.7, 0.020, 520, 0.075, 1.6, 95, 42, 0.13, 1.7, 0.22, 1.1, 0.75, 0.75, 2400, 2.2, 4.0)


def pump():
    """Shotgun pump-rack: two mechanical clacks (back, forward)."""
    rng = random.Random(51)
    out = zeros(0.36)
    for at, pitch, g in ((0.0, 1.0, 1.0), (0.15, 1.18, 0.85)):
        n = int(0.08 * SR)
        nz = noise(n, rng)
        clack = mul(biquad(nz, "hp", 1800), env_exp(n, 0.0018, attack=0.0001))
        ring = mul(biquad(biquad(nz, "bp", 3300 * pitch, 6), "bp", 3300 * pitch, 6), env_exp(n, 0.012))
        knock = mul(tone(n, 260 * pitch, 160 * pitch, 0.01), env_exp(n, 0.014, attack=0.0005))
        slide = mul(biquad(noise(n, rng), "bp", 1200, 1.2), env_exp(n, 0.02, attack=0.006))
        add_into(out, clack, 0.9 * g, at)
        add_into(out, ring, 1.6 * g, at)
        add_into(out, knock, 0.55 * g, at)
        add_into(out, slide, 0.35 * g, max(0.0, at - 0.012))
    wet = reverb(out, 0.12, 4000, 0.36)
    return fade_tail([d + w * 0.18 for d, w in zip(out, wet)])


# ---------------------------------------------------------------- goo
def bubble(n, f0, f1, tau):
    """A gloop: short sine whose pitch RISES from f0 to f1 over tau (a bubble closing), percussive envelope."""
    out, ph = [], 0.0
    for i in range(n):
        f = f0 + (f1 - f0) * min(1.0, i / (tau * SR))
        ph += 2 * math.pi * f / SR
        out.append(math.sin(ph))
    return mul(out, env_exp(n, tau * 0.8, attack=0.0015))


def squish(seed):
    rng = random.Random(seed)
    length = 0.62
    n = int(length * SR)
    out = zeros(length)
    # wet splat: noise through a closing low-pass, a fast attack and a fleshy decay
    splat = sweep_lp(noise(n, rng), 5200 + rng.uniform(-600, 600), 380, 0.045)
    add_into(out, mul(splat, env_exp(n, 0.09, attack=0.002)), 2.6)
    # mid "slap" band
    add_into(out, mul(biquad(noise(n, rng), "bp", 700 + rng.uniform(-120, 120), 1.4), env_exp(n, 0.03, attack=0.001)), 1.0)
    # pitch-dropping, wobbling body (the sack bursting)
    body = tone(n, 330 + rng.uniform(-40, 40), 75, 0.06, wobble=0.18, wobble_hz=31 + rng.uniform(-5, 5))
    add_into(out, mul(body, env_exp(n, 0.11, attack=0.003)), 0.85)
    # gloops as the goo sloshes
    for _ in range(rng.randint(3, 5)):
        at = rng.uniform(0.03, 0.2)
        m = int(0.05 * SR)
        add_into(out, bubble(m, rng.uniform(220, 420), rng.uniform(600, 1100), 0.03), rng.uniform(0.15, 0.32), at)
    # two droplets hitting the floor (the game adds more, timed to the blobs that splat)
    for at in (rng.uniform(0.26, 0.33), rng.uniform(0.38, 0.47)):
        m = int(0.045 * SR)
        add_into(out, bubble(m, rng.uniform(700, 900), rng.uniform(1300, 1800), 0.022), 0.22, at)
    wet = reverb(out, 0.18, 2600, length)
    mix = [d + w * 0.22 for d, w in zip(out, wet)]
    return fade_tail(soft_clip(mix, 1.3))


def hit(seed):
    """Bullet into a Drone: a small wet thwack."""
    rng = random.Random(seed)
    length = 0.16
    n = int(length * SR)
    out = zeros(length)
    add_into(out, mul(biquad(noise(n, rng), "hp", 3000), env_exp(n, 0.0012, attack=0.0001)), 0.5)
    add_into(out, mul(sweep_lp(noise(n, rng), 4200, 500, 0.012), env_exp(n, 0.022, attack=0.0008)), 2.0)
    add_into(out, mul(tone(n, 240 + rng.uniform(-30, 30), 110, 0.02), env_exp(n, 0.03, attack=0.001)), 0.7)
    m = int(0.04 * SR)
    add_into(out, bubble(m, rng.uniform(350, 500), rng.uniform(800, 1000), 0.02), 0.18, rng.uniform(0.01, 0.03))
    return fade_tail(soft_clip(out, 1.2), 0.02)


def drip(seed):
    rng = random.Random(seed)
    length = 0.11
    n = int(length * SR)
    out = zeros(length)
    add_into(out, bubble(int(0.07 * SR), rng.uniform(550, 800), rng.uniform(1300, 1900), 0.028), 1.0)
    add_into(out, mul(sweep_lp(noise(n, rng), 3000, 600, 0.01), env_exp(n, 0.008, attack=0.0005)), 0.6)
    return fade_tail(out, 0.02)


def main():
    print("Synthesizing into", os.path.normpath(OUT))
    write("rifle", normalize(rifle(), 0.95))
    write("smg", normalize(smg(), 0.9))
    write("shotgun", normalize(shotgun(), 0.98))
    write("shotgun_pump", normalize(pump(), 0.7))
    for i in range(3):
        write(f"squish_{i + 1}", normalize(squish(100 + i * 7), 0.95))
        write(f"hit_{i + 1}", normalize(hit(200 + i * 5), 0.8))
        write(f"drip_{i + 1}", normalize(drip(300 + i * 3), 0.6))


if __name__ == "__main__":
    main()
