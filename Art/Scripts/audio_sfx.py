"""Luge Rush audio: sled, track and UI effects. Each builder returns float samples."""
import numpy as np
from audio_dsp import *


# ---------------------------------------------------------------------- sled loops (mono)

def sled_roll():
    r = rng_for('roll')
    n = 8 * SR
    t = secs(n)
    body = resonate(bp(brown(n, r), 35, 1000, 2), [(105, 2.5, 3.0), (235, 3, 1.5), (610, 2.5, 1.0)])
    body = body / body.std() * (0.72 + 0.28 * lfo(n, 2.2, r)) * (0.85 + 0.15 * lfo(n, 11, r))
    grit = bp(white(n, r), 1100, 5200, 2)
    grit = grit / grit.std() * (0.45 + 0.55 * lfo(n, 16, r)) * (0.6 + 0.4 * lfo(n, 1.3, r))
    f0 = loop_hz(73, n)
    wob = 3.0 * (lfo(n, 1.5, r) - 0.5)
    hum = sum(np.sin(TAU * k * f0 * t + k * wob + r.uniform(0, TAU)) / k ** 1.2 for k in range(1, 7))
    hum = hum * (0.7 + 0.3 * lfo(n, 4, r))
    ticks = np.zeros(n)
    for _ in range(110):
        ev = bp(white(320, r), 1800, 4800, 2) * exp_env(320, 0.0016)
        place(ticks, ev, r.integers(n), r.uniform(0.15, 1.0), loop=True)
    return body + 0.20 * grit + 0.10 * hum + 0.35 * ticks


def sled_wind():
    r = rng_for('wind')
    n = 12 * SR
    src = bp(pink(n, r), 120, 3500, 1)
    whoosh = tv_filter(src, 480 + 900 * lfo(n, 0.22, r), 0.7, loop=True)
    air = bp(pink(n, r), 200, 900, 1)
    whistle = tv_filter(white(n, r), 1500 + 500 * lfo(n, 0.18, r), 10, loop=True)
    gust = 0.55 + 0.45 * lfo(n, 0.3, r)
    return (whoosh / whoosh.std() + 0.8 * air / air.std()) * gust + 0.10 * whistle / whistle.std() * gust ** 3


def sled_brake():
    r = rng_for('brake')
    n = int(3.5 * SR)
    t = secs(n)
    rub = resonate(bp(white(n, r), 650, 5200, 2), [(1900, 4, 2.2), (3100, 5, 1.6)])
    rub = rub / rub.std() * (0.4 + 0.6 * lfo(n, 34, r)) * (0.75 + 0.25 * lfo(n, 3, r))
    low = bp(brown(n, r), 160, 700, 2)
    low = low / low.std() * (0.5 + 0.5 * lfo(n, 21, r))
    wob = 1.8 * (lfo(n, 5, r) - 0.5)
    squeal = np.sin(TAU * loop_hz(2340, n) * t + 2 * wob) + 0.5 * np.sin(TAU * loop_hz(3515, n) * t + 3 * wob)
    return rub + 0.55 * low + 0.10 * squeal * lfo(n, 1.2, r) ** 2


def sled_drift():
    r = rng_for('drift')
    n = 3 * SR
    t = secs(n)
    hiss = bp(white(n, r), 1700, 10500, 2)
    hiss = hiss / hiss.std() * (0.5 + 0.5 * lfo(n, 24, r))
    crackle = np.zeros(n)
    density = lfo(n, 6, r)
    for _ in range(520):
        p = r.integers(n)
        if r.uniform() < density[p]:
            m = int(r.integers(90, 260))
            place(crackle, white(m, r) * exp_env(m, 0.0014), p, r.uniform(0.3, 1.0), loop=True)
    crackle = bp(crackle, 2300, 8500, 2)
    rubber = tv_filter(white(n, r), 870 + 170 * lfo(n, 1.6, r), 7, loop=True)
    tone = np.sin(TAU * loop_hz(930, n) * t + 2.5 * (lfo(n, 7, r) - 0.5))
    return hiss * 0.8 + crackle / crackle.std() * 0.9 + rubber / rubber.std() * 0.38 + 0.05 * tone


def sled_scrape():
    r = rng_for('scrape')
    n = 3 * SR
    grind = bp(white(n, r), 900, 6500, 2)
    grind = grind / grind.std() * (0.25 + 0.75 * lfo(n, 46, r) ** 2)
    hits = np.zeros(n)
    for _ in range(240):
        place(hits, white(60, r) * np.hanning(60), r.integers(n), r.uniform(0.2, 1.0), loop=True)
    ir = partials(int(0.35 * SR), [412, 985, 1725, 2640, 3915, 5410],
                  [1.0, 0.9, 0.8, 0.6, 0.45, 0.3], [0.09, 0.08, 0.06, 0.05, 0.035, 0.025], r)
    metal = convolve(hits, ir, True)
    rumble = bp(brown(n, r), 90, 420, 2)
    return grind * 0.75 + metal / metal.std() * 0.85 + rumble / rumble.std() * 0.45


# ---------------------------------------------------------------------- sled one-shots (mono)

def sled_boost():
    r = rng_for('boost')
    n = int(1.7 * SR)
    t = secs(n)
    u = t / t[-1]
    fc = 350 + 6000 * np.minimum(u / 0.18, 1) ** 2 - 4800 * np.clip((u - 0.18) / 0.82, 0, 1) ** 0.7
    air = tv_filter(pink(n, r), fc, 0.8) * (np.minimum(t / 0.012, 1) * np.exp(-t / 0.55))
    thump = sweep(150, 42, n) * exp_env(n, 0.13, 0.001)
    f = 90 + 620 * np.minimum(u / 0.22, 1) ** 1.5 - 260 * np.clip((u - 0.22) / 0.78, 0, 1)
    ph = TAU * np.cumsum(f) / SR
    rise = sum(np.sin(k * ph) / k for k in range(1, 9)) * np.minimum(t / 0.02, 1) * np.exp(-t / 0.33)
    flame = bp(white(n, r), 500, 5000, 1) * (lfo(n, 45, r) ** 2) * np.exp(-t / 0.28)
    dry = fade(air / air.std() * 0.9 + thump * 2.2 + rise * 0.45 + flame / flame.std() * 0.6, 0, 0.2)
    return trim_tail(reverb(dry, make_ir(0.8, r, stereo=False, bright=0.4), 0.14))


def impact(name, weight):
    """weight 0..1: light bump to heavy crash."""
    r = rng_for(name)
    n = int((0.55 + 0.75 * weight) * SR)
    thump = sweep(120 + 80 * weight, 42, n) * exp_env(n, 0.05 + 0.1 * weight, 0.0006)
    crack = bp(white(n, r), 700, 5200, 2) * exp_env(n, 0.012 + 0.025 * weight)
    plastic = partials(n, [318, 527, 893, 1312, 1880], [1, .8, .7, .45, .3],
                       [.05, .05, .04, .03, .02], r) * (0.7 + 0.5 * weight)
    metal = partials(n, r.uniform(0.92, 1.08) * np.array([1190, 1865, 2960, 4410]),
                     [.8, .7, .5, .35], [.2, .16, .11, .07], r) * (0.12 + 0.5 * weight)
    deb = np.zeros(n)
    for _ in range(int(3 + 12 * weight)):
        m = int(r.integers(160, 500))
        ev = bp(white(m, r), 1400, 6500, 2) * exp_env(m, 0.003)
        place(deb, ev, r.uniform(0.04, 0.45 + 0.3 * weight) * SR, r.uniform(0.2, 0.8))
    dry = fade(thump * 1.3 + crack / crack[:2000].std() * 0.75 + plastic * 0.8 + metal + deb * 0.5 * weight, 0, 0.12)
    return trim_tail(reverb(dry, make_ir(0.5, r, stereo=False, bright=0.6), 0.10))


# ---------------------------------------------------------------------- track

def lift_chain():
    r = rng_for('lift')
    n = int(4.2 * SR)
    t = secs(n)
    links = 28
    clacks = []
    for _ in range(4):
        m = int(0.06 * SR)
        body = partials(m, np.array([880, 1460, 2630, 3980]) * r.uniform(0.94, 1.06, 4),
                        [.9, .7, .5, .3], [.012, .01, .007, .005], r)
        click = bp(white(m, r), 2000, 7500, 2) * exp_env(m, 0.0025)
        thud = sweep(240, 110, m) * exp_env(m, 0.009)
        clacks.append(body + 0.55 * click / click.std() + 0.9 * thud)
    buf = np.zeros(n)
    for i in range(links):
        pos = (i + r.uniform(-0.05, 0.05)) * n / links
        place(buf, clacks[i % 4], pos, (1.0 if i % 2 == 0 else 0.68) * r.uniform(0.85, 1.05), loop=True)
    f0 = loop_hz(98, n)
    hum = sum(np.sin(TAU * k * f0 * t + r.uniform(0, TAU)) / k for k in (1, 2, 3, 5))
    rattle = bp(white(n, r), 320, 1900, 2) * (0.3 + 0.7 * lfo(n, 14, r))
    mix = buf / buf.std() + 0.16 * hum + 0.20 * rattle / rattle.std()
    return reverb(mix, make_ir(0.35, r, stereo=False, bright=0.6), 0.12, loop=True)


def _ring(freq, dur, harmonics, r, decay=None):
    """Bright detuned tone with a noise transient: never a plain sine."""
    n = int(dur * SR)
    t = secs(n)
    out = np.zeros(n)
    for det in (0.9985, 1.0015):
        for k, a in harmonics:
            if k * freq < 16000:
                out += a * np.sin(TAU * k * freq * det * t + r.uniform(0, TAU)) * np.exp(-t * (k - 1) * 3.5)
    env = np.minimum(t / 0.004, 1) * np.exp(-t / (decay if decay else dur * 0.45))
    tick = bp(white(n, r), 1500, 8000, 2) * exp_env(n, 0.004)
    return fade(out * env + tick * 0.5, 0, dur * 0.35)


SQUARE = [(1, 1), (3, .36), (5, .2), (7, .11), (9, .06)]
BRASS = [(1, 1), (2, .7), (3, .55), (4, .35), (5, .22), (6, .14), (7, .08)]
BELL = [(1, 1), (2.01, .5), (3.03, .28), (4.2, .16), (5.43, .08)]


def race_beep():
    r = rng_for('beep')
    x = _ring(660, 0.34, SQUARE, r, 0.14) + 0.4 * _ring(1320, 0.34, BELL, r, 0.08)
    return trim_tail(reverb(x, make_ir(0.6, r, stereo=False), 0.16))


def race_go():
    r = rng_for('go')
    n = int(1.15 * SR)
    t = secs(n)
    x = np.zeros(n)
    for f in (880, 1108.7, 1318.5):
        x += _ring(f, 1.15, BRASS, r, 0.42)
    x += 0.8 * _ring(1760, 1.15, BELL, r, 0.3)
    wh = tv_filter(pink(n, r), 800 + 5000 * np.exp(-t / 0.2), 0.8) * exp_env(n, 0.22, 0.004)
    x = fade(x / np.abs(x).max() + 0.15 * wh / wh.std() + sweep(180, 50, n) * exp_env(n, 0.1) * 0.6, 0, 0.1)
    return trim_tail(reverb(x, make_ir(0.9, r, stereo=False), 0.2))


def _seq(notes, tail, r, ir_s=0.9, wet=0.2):
    """notes: [(start_s, [freqs], dur, harmonics, gain, decay)]"""
    n = int((max(s + d for s, _, d, _, _, _ in notes) + tail) * SR)
    x = np.zeros(n)
    for s, freqs, d, harm, g, dec in notes:
        for f in freqs:
            place(x, _ring(f, d, harm, r, dec), s * SR, g)
    return reverb(x / np.abs(x).max(), make_ir(ir_s, r, stereo=True), wet)


def lap_complete():
    r = rng_for('lap')
    return trim_tail(_seq([(0, [783.99], .5, BELL, 1, .22), (.09, [987.77], .5, BELL, 1, .22),
                           (.18, [1174.66, 1567.98], .8, BELL, 1.1, .32)], .4, r))


def lap_final():
    r = rng_for('final')
    return trim_tail(_seq([(0, [587.33], .22, BRASS, .9, .1), (.12, [587.33], .22, BRASS, .9, .1),
                           (.24, [587.33], .22, BRASS, .9, .1), (.36, [880, 1174.66], .9, BRASS, 1.2, .34),
                           (.36, [1760], .9, BELL, .7, .3)], .4, r, wet=.24))


def race_finish():
    r = rng_for('finish')
    x = _seq([(0, [523.25, 659.25, 783.99], .4, BRASS, 1, .16),
              (.2, [587.33, 739.99, 880], .4, BRASS, 1, .16),
              (.4, [659.25, 830.61, 987.77], .4, BRASS, 1, .16),
              (.62, [698.46, 880, 1046.5, 1396.9], 2.0, BRASS, 1.3, .8),
              (.62, [2093], 2.0, BELL, .8, .7)], .8, r, 1.4, .26)
    n = len(x)
    t = secs(n)
    cym = bp(white(n, r), 4000, 13000, 1) * np.where(t > .62, np.exp(-(t - .62) / .7), 0)
    return trim_tail(x + 0.12 * np.stack([cym, np.roll(cym, 37)], axis=1) / cym.std())


# ---------------------------------------------------------------------- UI

def ui_nav():
    r = rng_for('nav')
    n = int(0.09 * SR)
    t = secs(n)
    tone = (sweep(1500, 1050, n) + 0.3 * np.sin(TAU * 2600 * t)) * exp_env(n, 0.016, 0.0008)
    click = bp(white(n, r), 2500, 9000, 2) * exp_env(n, 0.0025)
    return fade(tone + 0.3 * click / click[:200].std(), 0, 0.01)


def _marimba(freq, dur, r):
    n = int(dur * SR)
    t = secs(n)
    x = np.sin(TAU * freq * t) + 0.5 * np.sin(TAU * freq * 3.93 * t) * np.exp(-t / 0.018) \
        + 0.28 * np.sin(TAU * freq * 2 * t) * np.exp(-t / 0.05)
    tick = bp(white(n, r), 1500, 7000, 2) * exp_env(n, 0.002)
    return fade(x * np.minimum(t / 0.0015, 1) * np.exp(-t / (dur * 0.33)) + 0.1 * tick / tick[:200].std(), 0, dur * 0.4)


def _ui_seq(name, freqs, gap, dur, wet=0.12):
    r = rng_for(name)
    n = int((gap * len(freqs) + dur + 0.1) * SR)
    x = np.zeros(n)
    for i, f in enumerate(freqs):
        place(x, _marimba(f, dur, r), i * gap * SR, 1 + 0.12 * i)
    return trim_tail(reverb(x, make_ir(0.45, r, stereo=True), wet))


def ui_confirm():
    return _ui_seq('confirm', [880, 1318.5], 0.075, 0.28)


def ui_back():
    return _ui_seq('back', [659.25, 440], 0.075, 0.26)


def ui_notify():
    return _ui_seq('notify', [783.99, 1046.5, 1318.5], 0.11, 0.5, 0.2)
