"""Luge Rush audio: stereo ambience loops (forest, mountain, canyon)."""
import numpy as np
from audio_dsp import *

LEN = 40 * SR


def _bed(n, r, lo, hi, tone=pink, order=1):
    """Decorrelated stereo noise band."""
    x = np.stack([bp(tone(n, r), lo, hi, order), bp(tone(n, r), lo, hi, order)], axis=1)
    return x / x.std()


def _chirp(f0, f1, dur, harm=0.18, bend=1.0):
    n = int(dur * SR)
    u = np.linspace(0, 1, n)
    ph = TAU * np.cumsum(f0 + (f1 - f0) * u ** bend) / SR
    return (np.sin(ph) + harm * np.sin(2 * ph)) * np.sin(np.pi * u) ** 1.5


def _warbler(r):
    base = r.uniform(3000, 4700)
    x = []
    for _ in range(r.integers(5, 12)):
        x.append(_chirp(base * r.uniform(0.85, 1.2), base * r.uniform(0.85, 1.25), r.uniform(0.04, 0.09)))
        x.append(np.zeros(int(r.uniform(0.012, 0.05) * SR)))
    return np.concatenate(x)


def _whistler(r):
    a = r.uniform(1800, 2500)
    return np.concatenate([_chirp(a * 1.12, a, 0.26, 0.08), np.zeros(int(0.09 * SR)),
                           _chirp(a * 0.86, a * 0.8, 0.36, 0.08), np.zeros(int(0.07 * SR)),
                           _chirp(a, a * 0.9, 0.2, 0.08)])


def _trill(r):
    n = int(r.uniform(0.5, 1.1) * SR)
    t = secs(n)
    f = r.uniform(4200, 5200)
    return np.sin(TAU * f * t + 1.2 * np.sin(TAU * 7 * t)) * (0.5 + 0.5 * np.sin(TAU * 26 * t)) ** 2 * np.sin(np.pi * t / t[-1])


def forest():
    r = rng_for('amb_forest')
    n = LEN
    gust = lfo(n, 0.14, r) ** 1.6
    leaves = _bed(n, r, 1600, 8500) * ((0.25 + 1.2 * gust) * (0.6 + 0.4 * lfo(n, 5, r)))[:, None]
    breeze = _bed(n, r, 90, 520) * (0.45 + 0.55 * gust)[:, None]
    birds = np.zeros((n, 2))
    calls = [_warbler] * 14 + [_whistler] * 8 + [_trill] * 5
    order = r.permutation(len(calls))
    for i, c in enumerate(order):
        place(birds, calls[c](r), (i + r.uniform(0, 0.8)) * n / len(calls), r.uniform(0.25, 1.0),
              pan=r.uniform(-0.85, 0.85), loop=True)
    birds = reverb(bp(birds, 900, 11000, 1), make_ir(1.6, r, bright=0.8), 0.32, loop=True)
    return 0.5 * leaves + 0.32 * breeze + 2.7 * birds / np.abs(birds).max()


def alpine():
    r = rng_for('amb_alpine')
    n = LEN
    src = _bed(n, r, 120, 2600)
    wind = np.stack([tv_filter(src[:, i], 350 + 750 * lfo(n, 0.1, r), 0.8, loop=True) for i in (0, 1)], axis=1)
    gust = (0.35 + 0.65 * lfo(n, 0.09, r) ** 1.4)[:, None]
    whistle = tv_filter(white(n, r), 1300 + 600 * lfo(n, 0.07, r), 12, loop=True)
    water = reverb(_bed(n, r, 170, 3200) * (0.92 + 0.08 * lfo(n, 0.6, r))[:, None],
                   make_ir(1.2, r, bright=0.3), 0.5, loop=True)
    water = spec(water, lambda f: 1.0 / np.sqrt(1 + (f / 1600) ** 2))
    return wind / wind.std() * gust + 0.05 * (whistle / whistle.std() * lfo(n, 0.12, r) ** 3)[:, None] \
        + 0.55 * water / water.std()


def canyon():
    r = rng_for('amb_canyon')
    n = LEN
    t = secs(n)
    src = _bed(n, r, 200, 3400)
    wind = np.stack([tv_filter(src[:, i], 700 + 1000 * lfo(n, 0.12, r), 1.5, loop=True) for i in (0, 1)], axis=1)
    gust = (0.3 + 0.7 * lfo(n, 0.1, r) ** 1.8)[:, None]
    sand = _bed(n, r, 3800, 10500, white) * (lfo(n, 0.14, r) ** 2.5)[:, None]
    insects = np.zeros((n, 2))
    # Cicada: narrow buzzing band, pulsed, swelling in long waves.
    for pan, hz, rate in ((-0.5, 4400, 68), (0.6, 5150, 74)):
        c = bp(white(n, r), hz * 0.93, hz * 1.07, 4)
        pulse = (0.5 + 0.5 * np.sin(TAU * loop_hz(rate, n) * t)) ** 3
        place(insects, c / c.std() * pulse * lfo(n, 0.06, r) ** 2, 0, 0.5, pan=pan, loop=True)
    # Crickets: little three-pulse chirps.
    chirp = np.zeros(int(0.17 * SR))
    for k in range(3):
        m = int(0.032 * SR)
        place(chirp, np.sin(TAU * 3650 * secs(m)) * np.hanning(m), k * 0.05 * SR)
    for i in range(46):
        place(insects, chirp, i * n / 46 + r.uniform(-400, 400), r.uniform(0.25, 0.6), pan=-0.75, loop=True)
    for i in range(31):
        place(insects, chirp, i * n / 31 + r.uniform(-400, 400), r.uniform(0.15, 0.4), pan=0.8, loop=True)
    insects = reverb(insects, make_ir(0.9, r, bright=0.7), 0.25, loop=True)
    low = _bed(n, r, 45, 260) * (0.5 + 0.5 * lfo(n, 0.1, r))[:, None]
    return wind / wind.std() * gust + 0.1 * sand + 0.34 * insects + 0.35 * low
