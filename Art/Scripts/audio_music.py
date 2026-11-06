"""Luge Rush audio: original menu and race music loops, written note by note.

The whole render is circular: note tails, delay and reverb wrap into the start of the loop.
"""
import numpy as np
from audio_dsp import *

NOTE = {'C': 0, 'C#': 1, 'Db': 1, 'D': 2, 'D#': 3, 'Eb': 3, 'E': 4, 'F': 5, 'F#': 6, 'Gb': 6,
        'G': 7, 'G#': 8, 'Ab': 8, 'A': 9, 'A#': 10, 'Bb': 10, 'B': 11}


def midi(name):
    return NOTE[name[:-1]] + 12 * (int(name[-1]) + 1)


def hz(m):
    return 440.0 * 2 ** ((m - 69) / 12)


def parse_bar(text):
    """'A4:3 r:2 D5:3' -> [(step, length, midi)] in 16th notes."""
    out, step = [], 0
    for tok in text.split():
        name, length = tok.split(':')
        if name != 'r':
            out.append((step, int(length), midi(name)))
        step += int(length)
    assert step == 16, text
    return out


# ---------------------------------------------------------------------- instruments

_cache = {}


def cached(fn):
    def wrap(*a):
        key = (fn.__name__,) + a
        if key not in _cache:
            _cache[key] = fn(*a)
        return _cache[key]
    return wrap


def _saw(f, n, fc_env, harmonics=26, detune=0.0, phase=0.0):
    t = secs(n)
    ph = TAU * f * (1 + detune) * t + phase
    out = np.zeros(n)
    for k in range(1, harmonics + 1):
        if k * f > 15000:
            break
        out += np.sin(k * ph) / k * np.exp(-(k * f / fc_env) ** 2)
    return out


@cached
def bass(m, n):
    f = hz(m)
    t = secs(n)
    fc = 330 + 1900 * np.exp(-t / 0.075)
    x = 0.8 * _saw(f, n, fc) + 0.8 * np.sin(TAU * f * t)
    return fade(x * np.minimum(t / 0.003, 1) * np.exp(-t / 0.45), 0, 0.012)


@cached
def pluck(m, n):
    f = hz(m)
    t = secs(n)
    idx = 2.8 * np.exp(-t / 0.05)
    x = np.sin(TAU * f * t + idx * np.sin(TAU * 2 * f * t) + 0.5 * idx * np.sin(TAU * 5 * f * t))
    return fade(x * np.minimum(t / 0.002, 1) * np.exp(-t / 0.2), 0, 0.01)


@cached
def lead(m, n):
    f = hz(m)
    t = secs(n)
    vib = 1 + 0.004 * np.sin(TAU * 5.5 * t) * np.minimum(t / 0.25, 1)
    fc = 2600 + 2200 * np.exp(-t / 0.08)
    ph = TAU * np.cumsum(f * vib) / SR
    x = np.zeros(n)
    for det, off in ((1.0, 0.0), (1.0035, 1.1), (0.9965, 2.3)):
        for k in range(1, 14):
            if k * f > 13000:
                break
            x += np.sin(k * ph * det + k * off) / k * np.exp(-(k * f / fc) ** 2)
    return fade(x * np.minimum(t / 0.006, 1) * (0.72 + 0.28 * np.exp(-t / 0.12)), 0, 0.025)


@cached
def bell_lead(m, n):
    f = hz(m)
    t = secs(n)
    idx = 1.7 * np.exp(-t / 0.18)
    x = np.sin(TAU * f * t + idx * np.sin(TAU * 3.0 * f * t)) + 0.3 * np.sin(TAU * 2 * f * t)
    return fade(x * np.minimum(t / 0.003, 1) * np.exp(-t / 0.5), 0, 0.02)


@cached
def pad(chord, n, r_phase):
    x = np.zeros(n)
    t = secs(n)
    for i, m in enumerate(chord):
        f = hz(m)
        for j, det in enumerate((-0.0028, 0.0, 0.0031)):
            x += _saw(f, n, 1500.0, 10, det, r_phase + i * 1.3 + j * 2.1)
    env = np.minimum(t / 0.09, 1) * np.minimum((t[-1] - t) / 0.08, 1)
    return x * env


@cached
def kick(n):
    t = secs(n)
    click = bp(np.random.default_rng(1).standard_normal(n), 1200, 6000, 2) * np.exp(-t / 0.0022) * 0.18
    return sweep(170, 46, n, 6.0) * np.exp(-t / 0.15) + click


@cached
def snare(n):
    r = np.random.default_rng(2)
    t = secs(n)
    tone = np.sin(TAU * 196 * t) * np.exp(-t / 0.06) + 0.5 * np.sin(TAU * 330 * t) * np.exp(-t / 0.035)
    noise = bp(r.standard_normal(n), 1100, 8200, 1) * np.exp(-t / 0.085)
    return 0.7 * tone + 0.42 * noise / noise[:600].std()


@cached
def clap(n):
    r = np.random.default_rng(3)
    t = secs(n)
    env = sum(np.exp(-np.maximum(t - d, 0) / 0.006) * (t >= d) for d in (0, 0.011, 0.022)) + 0.8 * np.exp(-np.maximum(t - 0.03, 0) / 0.08) * (t >= 0.03)
    x = bp(r.standard_normal(n), 800, 3800, 2) * env
    return x / x.std() * 0.3


@cached
def hat(n, tau):
    r = np.random.default_rng(4)
    x = bp(r.standard_normal(n), 7000, 14500, 3)
    return x / x.std() * np.exp(-secs(n) / tau) * 0.55


@cached
def crash(n):
    r = np.random.default_rng(5)
    x = bp(r.standard_normal(n), 3200, 13000, 1)
    return x / x.std() * np.exp(-secs(n) / 0.9) * 0.5


@cached
def tom(m, n):
    t = secs(n)
    return sweep(hz(m) * 1.8, hz(m), n, 5.0) * np.exp(-t / 0.13)


def riser(n, r):
    u = np.linspace(0, 1, n)
    x = tv_filter(pink(n, r), 300 + 7000 * u ** 2.2, 1.1)
    return x / x.std() * u ** 2.4 * np.minimum((1 - u) * 60, 1)


# ---------------------------------------------------------------------- song frame

class Song:
    def __init__(self, bpm, bars, seed):
        self.step = 60.0 / bpm / 4 * SR
        self.bars = bars
        self.n = int(round(bars * 16 * self.step))
        self.rng = rng_for(seed)
        self.bus = {}

    def buf(self, name):
        return self.bus.setdefault(name, np.zeros((self.n, 2)))

    def at(self, bar, step):
        return (bar * 16 + step) * self.step

    def hit(self, name, ev, bar, step, gain=1.0, pan=0.0, swing=0):
        place(self.buf(name), ev, self.at(bar, step) + swing, gain, pan=pan, loop=True)

    def len(self, steps):
        return int(steps * self.step)

    def delay(self, x, steps, feedback, repeats=4):
        out = np.zeros_like(x)
        d = int(steps * self.step)
        for i in range(1, repeats + 1):
            y = np.roll(x, d * i, axis=0) * feedback ** (i - 1)
            out += y[:, ::-1] if i % 2 else y
        return out

    def sidechain(self, kicks, depth=0.5, tau=0.16):
        """Volume pump following the kick pattern (circular)."""
        env = np.zeros(self.n)
        shape = 1 - depth * np.exp(-secs(int(0.5 * SR)) / tau)
        env[:] = 1.0
        for k in kicks:
            idx = (int(k) + np.arange(len(shape))) % self.n
            env[idx] = np.minimum(env[idx], shape)
        return env[:, None]


def render(song, gains, kicks, wet, delay_bus=None):
    ir = make_ir(1.5, song.rng, bright=0.55)
    pump = song.sidechain(kicks)
    send = np.zeros((song.n, 2))
    mix = np.zeros((song.n, 2))
    for name, x in song.bus.items():
        y = x * gains.get(name, 1.0)
        if name in ('pad', 'bass', 'pluck', 'keys'):
            y = y * pump
        if delay_bus and name in delay_bus:
            y = y + song.delay(y, delay_bus[name][0], delay_bus[name][1]) * delay_bus[name][2]
        mix += y
        send += y * wet.get(name, 0.0)
    mix += convolve(send, ir, True)
    mix = spec(mix, lambda f: 1.0 / np.sqrt(1 + (28 / f) ** 4))
    return mix


# ---------------------------------------------------------------------- race: 136 BPM, D minor

RACE_A = ['A4:3 D5:3 F5:2 E5:2 D5:2 A4:2 r:2', 'Bb4:3 D5:3 F5:2 G5:4 F5:2 D5:2',
          'C5:3 F5:3 A5:2 G5:2 F5:2 E5:2 F5:2', 'G5:6 E5:2 C5:4 r:4',
          'A4:3 D5:3 F5:2 A5:3 G5:3 F5:2', 'Bb5:4 A5:2 G5:2 F5:4 D5:4',
          'C6:3 A5:3 F5:2 G5:3 A5:3 F5:2', 'E5:4 G5:4 C6:6 r:2']
RACE_B = ['D6:2 Bb5:2 G5:2 D5:2 G5:3 A5:3 Bb5:2', 'A5:6 F5:2 D5:4 F5:4',
          'D6:2 Bb5:2 F5:2 D5:2 F5:3 G5:3 A5:2', 'G5:2 A5:2 C#6:4 E6:6 r:2',
          'D6:3 C6:3 Bb5:2 C6:3 Bb5:3 A5:2', 'A5:4 D6:4 F5:4 A5:4',
          'G5:3 A5:3 Bb5:2 C6:3 D6:3 E6:2', 'E6:8 C#6:4 A5:4']
#            root      chord tones
DM = ('D2', ['D4', 'F4', 'A4'])
BB = ('Bb1', ['Bb3', 'D4', 'F4'])
FM = ('F2', ['A3', 'C4', 'F4'])
CM = ('C2', ['G3', 'C4', 'E4'])
GM = ('G1', ['Bb3', 'D4', 'G4'])
AM = ('A1', ['A3', 'C#4', 'E4'])


def _drums_race(s, bar, level, kicks):
    """level 0: hats only, 1: half time, 2: full drive."""
    L16 = s.len(1)
    for st in range(0, 16, 2 if level < 2 else 1):
        g = (0.55 if st % 4 == 2 else 0.32) * (0.8 if st % 2 else 1.0)
        s.hit('hat', hat(L16 * 2, 0.022 if st % 4 != 2 else 0.05), bar, st, g, pan=0.18)
    if level >= 2:
        for st in (0, 4, 8, 12, 14 if bar % 2 else 12):
            s.hit('kick', kick(s.len(6)), bar, st)
            kicks.add(s.at(bar, st))
        s.hit('snare', snare(s.len(6)), bar, 4)
        s.hit('snare', snare(s.len(6)), bar, 12)
        if bar % 4 == 3:
            s.hit('snare', snare(s.len(6)), bar, 15, 0.45)
    elif level == 1:
        for st in (0, 10):
            s.hit('kick', kick(s.len(6)), bar, st, 0.9)
            kicks.add(s.at(bar, st))
        s.hit('snare', snare(s.len(6)), bar, 8, 0.9)


def _chord_bar(s, bar, chord, bass_on=True, arp=True, padg=1.0):
    root, tones = chord
    m = midi(root)
    L = s.len(2)
    if bass_on:
        for i, st in enumerate(range(0, 16, 2)):
            note = m + (12 if i in (3, 6) else 0) + (7 if i == 5 else 0)
            s.hit('bass', bass(note, L), bar, st, 1.0 if i % 2 == 0 else 0.8)
    t = [midi(x) for x in tones]
    s.hit('pad', pad(tuple(t), s.len(16), 0.7), bar, 0, padg)
    if arp:
        pattern = [t[0], t[1], t[2], t[0] + 12, t[2], t[1], t[0] + 12, t[1] + 12]
        for i, st in enumerate(range(0, 16, 2)):
            s.hit('pluck', pluck(pattern[i] + 12, s.len(4)), bar, st + 1 if i % 4 == 3 else st,
                  0.75 if i % 2 else 1.0, pan=-0.35 if i % 2 else 0.35)


def _melody(s, bars, start, inst, bus, gain=1.0, shift=0, pan=0.0):
    for b, text in enumerate(bars):
        for st, length, m in parse_bar(text):
            s.hit(bus, inst(m + shift, s.len(length)), start + b, st, gain, pan=pan)


def music_race():
    s = Song(136, 40, 'music_race')
    kicks = set()
    prog_a = [DM, BB, FM, CM]
    prog_b = [GM, DM, BB, AM]
    for bar in range(40):
        if bar < 8:          # groove
            _drums_race(s, bar, 2 if bar >= 4 else 1, kicks)
            _chord_bar(s, bar, prog_a[bar % 4], True, True, 0.7)
        elif bar < 16:       # theme A
            _drums_race(s, bar, 2, kicks)
            _chord_bar(s, bar, prog_a[bar % 4], True, True, 0.8)
        elif bar < 24:       # theme B
            _drums_race(s, bar, 2, kicks)
            _chord_bar(s, bar, prog_b[bar % 4], True, True, 1.0)
        elif bar < 32:       # breakdown
            _drums_race(s, bar, 1 if bar < 28 else 0, kicks)
            _chord_bar(s, bar, [BB, CM, DM, DM][bar % 4], bar >= 28, True, 1.1)
        else:                # theme A again, doubled
            _drums_race(s, bar, 2, kicks)
            _chord_bar(s, bar, prog_a[bar % 4] if bar < 39 else AM, True, True, 1.0)
        if bar in (8, 16, 32):
            s.hit('crash', crash(s.len(32)), bar, 0, 0.7, pan=-0.2)
    _melody(s, RACE_A, 8, lead, 'lead')
    _melody(s, RACE_B, 16, lead, 'lead')
    _melody(s, RACE_A, 32, lead, 'lead')
    _melody(s, RACE_A, 32, bell_lead, 'keys', 0.5, 12, 0.2)
    # Breakdown call and answer: a fresh small phrase, not the theme.
    _melody(s, ['F5:3 G5:3 A5:2 r:8', 'G5:3 A5:3 C6:2 r:8', 'A5:4 D6:4 C6:2 A5:2 G5:4', 'F5:8 E5:4 D5:4'], 24, bell_lead, 'keys', 0.8)
    _melody(s, ['F5:3 G5:3 A5:2 r:2 A5:2 C6:4', 'G5:3 A5:3 C6:2 r:2 C6:2 E6:4', 'D6:2 C6:2 A5:2 G5:2 A5:4 D6:4', 'D6:16'], 28, bell_lead, 'keys', 0.9)
    # Fill and riser into the last section.
    r = riser(s.len(32), s.rng)
    place(s.buf('fx'), r, s.at(28, 0), 0.32, loop=True)
    for st, m in zip((8, 10, 12, 14), (50, 48, 45, 43)):
        s.hit('tom', tom(m, s.len(4)), 31, st, 0.9, pan=-0.3 + st * 0.03)
    mix = render(s, {'kick': 0.75, 'snare': 1.2, 'hat': 0.9, 'bass': 0.24, 'pad': 0.05, 'pluck': 0.22,
                     'lead': 0.2, 'keys': 0.5, 'crash': 0.6, 'tom': 0.6, 'fx': 1.0},
                 sorted(kicks),
                 {'snare': 0.1, 'pad': 0.2, 'pluck': 0.22, 'lead': 0.22, 'keys': 0.3, 'tom': 0.12},
                 {'lead': (3, 0.4, 0.3), 'pluck': (3, 0.35, 0.18)})
    return mix


# ---------------------------------------------------------------------- menu: 118 BPM, G major

MENU_A = ['D5:2 G5:2 B5:3 A5:3 G5:2 D5:2 E5:2', 'F#5:4 A5:3 D6:3 B5:2 A5:4',
          'G5:2 B5:2 E6:3 D6:3 B5:2 G5:2 A5:2', 'E5:4 G5:4 C6:4 r:4',
          'D6:3 B5:3 G5:2 A5:3 B5:3 D6:2', 'A5:6 F#5:2 A5:2 B5:2 D6:4',
          'E6:3 D6:3 B5:2 G5:3 E5:3 G5:2', 'G5:4 E5:4 D5:8']
MENU_B = ['E5:2 A5:2 C6:4 B5:2 A5:2 E5:4', 'G5:2 C6:2 E6:4 D6:2 C6:2 G5:4',
          'B5:3 D6:3 G6:2 F#6:2 D6:2 B5:4', 'A5:4 F#5:4 D6:8',
          'E5:2 A5:2 C6:3 D6:3 E6:2 C6:2 A5:2', 'G5:2 C6:2 E6:3 F#6:3 G6:2 E6:2 C6:2',
          'B5:3 D6:3 B5:2 A5:3 G5:3 A5:2', 'A5:6 B5:2 D6:8']
G_ = ('G1', ['B3', 'D4', 'G4'])
D_ = ('D2', ['A3', 'D4', 'F#4'])
EM = ('E2', ['B3', 'E4', 'G4'])
C_ = ('C2', ['G3', 'C4', 'E4'])
AMI = ('A1', ['A3', 'C4', 'E4'])


def _drums_menu(s, bar, full, kicks):
    for st in range(0, 16):
        if st % 2 == 0 or full:
            s.hit('hat', hat(s.len(2), 0.018 if st % 4 else 0.03), bar, st,
                  (0.42 if st % 4 == 2 else 0.24) * (0.75 if st % 2 else 1), pan=-0.2,
                  swing=int(s.step * 0.16) if st % 2 else 0)
    for st in ((0, 7, 10) if full else (0, 10)):
        s.hit('kick', kick(s.len(6)), bar, st, 0.95)
        kicks.add(s.at(bar, st))
    if full:
        s.hit('snare', clap(s.len(4)), bar, 4)
        s.hit('snare', clap(s.len(4)), bar, 12)


def _menu_bar(s, bar, chord, arp_oct=12):
    root, tones = chord
    m = midi(root)
    t = [midi(x) for x in tones]
    for st, off, g in ((0, 0, 1), (3, 0, .8), (6, 12, .7), (8, 7, .9), (11, 0, .8), (14, 12, .7)):
        s.hit('bass', bass(m + off, s.len(3)), bar, st, g)
    s.hit('pad', pad(tuple(t), s.len(16), 1.9), bar, 0, 0.9)
    pat = [0, 1, 2, 1, 0, 2, 1, 2]
    for i in range(8):
        n = t[pat[i]] + arp_oct + (12 if i in (3, 7) else 0)
        s.hit('pluck', pluck(n, s.len(5)), bar, i * 2 + (1 if i % 3 == 2 else 0),
              0.9 if i % 2 == 0 else 0.65, pan=0.4 if i % 2 else -0.4)


def music_menu():
    s = Song(118, 32, 'music_menu')
    kicks = set()
    a = [G_, D_, EM, C_]
    b = [AMI, C_, G_, D_]
    for bar in range(32):
        sect = bar // 8
        _drums_menu(s, bar, sect != 0 or bar >= 4, kicks)
        _menu_bar(s, bar, (b if sect == 2 else a)[bar % 4])
        if bar in (8, 16, 24):
            s.hit('crash', crash(s.len(24)), bar, 0, 0.5, pan=0.2)
    _melody(s, MENU_A, 8, bell_lead, 'keys')
    _melody(s, MENU_A, 8, lead, 'lead', 0.5)
    _melody(s, MENU_B, 16, bell_lead, 'keys')
    _melody(s, MENU_B, 16, lead, 'lead', 0.6)
    _melody(s, MENU_A, 24, bell_lead, 'keys')
    _melody(s, MENU_A, 24, lead, 'lead', 0.7, 12)
    return render(s, {'kick': 0.75, 'snare': 0.6, 'hat': 1.0, 'bass': 0.24, 'pad': 0.045, 'pluck': 0.22,
                      'lead': 0.12, 'keys': 0.3, 'crash': 0.5},
                  sorted(kicks),
                  {'snare': 0.1, 'pad': 0.22, 'pluck': 0.28, 'lead': 0.2, 'keys': 0.3},
                  {'keys': (3, 0.38, 0.28), 'pluck': (3, 0.3, 0.15)})
