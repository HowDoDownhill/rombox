"""Synthesises RomBox's interface sounds into assets/sounds/.

Run from the repository root: python tools/ui-sounds/make_ui_sounds.py
Each sound is a few sine partials under a fast envelope, so they stay short and quiet.
"""
import math
import struct
import wave
from pathlib import Path

RATE = 44100
PEAK = 0.45  # about -7 dBFS
OUT = Path("assets/sounds")


def tone(freq_start, freq_end, duration, decay, harmonics=((1, 1.0),), delay=0.0):
    """A pitch glide with a 2 ms attack and an exponential decay (time constant `decay`)."""
    total = int(RATE * (delay + duration))
    samples = [0.0] * total
    phase = [0.0] * len(harmonics)
    for i in range(int(RATE * delay), total):
        t = i / RATE - delay
        progress = t / duration
        freq = freq_start * (freq_end / freq_start) ** progress
        attack = min(1.0, t / 0.002)
        envelope = attack * math.exp(-t / decay)
        value = 0.0
        for k, (multiple, amplitude) in enumerate(harmonics):
            phase[k] += 2 * math.pi * freq * multiple / RATE
            value += amplitude * math.sin(phase[k])
        samples[i] = value * envelope
    return samples


def mix(*layers):
    length = max(len(layer) for layer in layers)
    return [sum(layer[i] for layer in layers if i < len(layer)) for i in range(length)]


def write(name, samples):
    peak = max(abs(s) for s in samples) or 1.0
    fade = int(RATE * 0.003)
    for i in range(fade):
        samples[-1 - i] *= i / fade
    frames = b"".join(struct.pack("<h", int(s / peak * PEAK * 32767)) for s in samples)
    OUT.mkdir(parents=True, exist_ok=True)
    with wave.open(str(OUT / name), "wb") as file:
        file.setnchannels(1)
        file.setsampwidth(2)
        file.setframerate(RATE)
        file.writeframes(frames)
    print(f"{OUT / name}: {len(samples) / RATE * 1000:.0f} ms")


soft = ((1, 1.0), (2, 0.18), (3, 0.05))

# Move: a short wooden tick.
write("move.wav", tone(1500, 1350, 0.035, 0.007, soft))

# Select: two rising notes, a fifth apart.
write("select.wav", mix(
    tone(784, 784, 0.05, 0.02, soft),
    tone(1175, 1175, 0.07, 0.025, soft, delay=0.035),
))

# Back: two falling notes.
write("back.wav", mix(
    tone(988, 988, 0.05, 0.02, soft),
    tone(659, 659, 0.07, 0.025, soft, delay=0.035),
))

# Switch (changing system): a soft upward sweep.
write("switch.wav", tone(420, 760, 0.07, 0.03, soft))
