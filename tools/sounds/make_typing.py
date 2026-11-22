"""Synthesizes soft, distant keyboard typing over a faint noise bed (GAD-T502).

Each keystroke is a few ms of band-passed noise with a short decay (a muffled tick), heavily low-passed and given a little
room tail so it reads as someone typing across the room, not at your ear. Rhythm: bursts of 3-12 keys at ~120-220 ms,
with pauses, an occasional softer space-bar thump. A quiet pink-ish noise bed runs underneath. The loop's ends fade
into the bed so it repeats seamlessly. Deterministic per seed.
"""
import sys
import wave

import numpy as np

SR = 44100


def one_pole_lowpass(x, cutoff):
    a = np.exp(-2 * np.pi * cutoff / SR)
    y = np.empty_like(x)
    acc = 0.0
    for i, v in enumerate(x):
        acc = (1 - a) * v + a * acc
        y[i] = acc
    return y


def keystroke(rng, thump=False):
    n = int(SR * (0.06 if thump else 0.035))
    t = np.arange(n) / SR
    noise = rng.standard_normal(n)
    if thump:
        body = one_pole_lowpass(noise, 700) * np.exp(-t * 70)
        return body * 0.8
    click = noise * np.exp(-t * 260)                       # the tick
    click = click - one_pole_lowpass(click, 900)          # high-pass a little: a tick, not a thud
    body = one_pole_lowpass(noise, 1500) * np.exp(-t * 120) * 0.5
    return (click * 0.6 + body) * rng.uniform(0.55, 1.0)


def make(seed, seconds):
    rng = np.random.default_rng(seed)
    n = int(SR * seconds)
    out = np.zeros(n + SR)
    pos = int(SR * rng.uniform(0.3, 0.8))
    while pos < n - SR:
        for _ in range(rng.integers(3, 13)):              # a burst of typing
            k = keystroke(rng, thump=rng.random() < 0.08)
            out[pos:pos + len(k)] += k
            pos += int(SR * rng.uniform(0.11, 0.23))
        pos += int(SR * rng.uniform(0.4, 1.6))            # a pause
    out = out[:n]
    out = one_pole_lowpass(out, 3200)                     # distance: lose the crisp top end
    # Small room: a few quiet, low-passed early reflections.
    room = np.zeros_like(out)
    for delay_ms, g in ((17, 0.35), (29, 0.25), (43, 0.18), (61, 0.12)):
        d = int(SR * delay_ms / 1000)
        room[d:] += out[:-d] * g
    out = out + one_pole_lowpass(room, 1800)
    # Faint pink-ish bed.
    bed = one_pole_lowpass(rng.standard_normal(n), 900) * 0.035
    out = out / (np.max(np.abs(out)) + 1e-9) * 0.5 + bed
    # Seamless loop: fade the keys (not the bed) out/in over the last/first 300 ms.
    f = int(SR * 0.3)
    ramp = np.linspace(0, 1, f)
    keys = out - bed
    keys[:f] *= ramp
    keys[-f:] *= ramp[::-1]
    out = keys + bed
    out = out / np.max(np.abs(out)) * 0.7
    return out


def write_wav(path, x):
    pcm = (np.clip(x, -1, 1) * 32767).astype(np.int16)
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())


if __name__ == "__main__":
    out_dir = sys.argv[1]
    for i, seed in enumerate((1005, 2210), start=1):
        write_wav(f"{out_dir}/typing-{i}.wav", make(seed, 20.0))
