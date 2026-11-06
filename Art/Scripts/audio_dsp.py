"""Luge Rush audio: shared DSP helpers (numpy only).

Everything that ends up in a loop is computed circularly (FFT filters, circular
convolution, wrap-around event placement), so loops are seamless by construction.
"""
import wave
import numpy as np

SR = 44100
TAU = 2.0 * np.pi


def secs(n):
    return np.arange(n) / SR


def rng_for(name):
    """Deterministic generator per sound name: builds are byte-reproducible."""
    return np.random.default_rng(sum((i + 1) * ord(c) for i, c in enumerate(name)) + 7919)


# --------------------------------------------------------------------- filters

def spec(x, gain_fn):
    """Circular zero-phase FFT filter. gain_fn(freqs_hz) -> magnitude."""
    x = np.asarray(x, float)
    if x.ndim == 2:
        return np.stack([spec(x[:, i], gain_fn) for i in range(x.shape[1])], axis=1)
    f = np.fft.rfftfreq(len(x), 1 / SR)
    f[0] = 1e-3
    return np.fft.irfft(np.fft.rfft(x) * gain_fn(f), len(x))


def bp_gain(lo, hi, order=2):
    def g(f):
        out = np.ones_like(f)
        if lo:
            out = out / np.sqrt(1 + (lo / f) ** (2 * order))
        if hi:
            out = out / np.sqrt(1 + (f / hi) ** (2 * order))
        return out
    return g


def bp(x, lo, hi, order=2):
    return spec(x, bp_gain(lo, hi, order))


def peak_gain(peaks):
    """peaks: [(freq, q, linear boost)] as log-frequency gaussians added to unity."""
    def g(f):
        out = np.ones_like(f)
        for fc, q, boost in peaks:
            w = 1.0 / (q * 2.0)
            out = out + boost * np.exp(-0.5 * (np.log2(f / fc) / w) ** 2)
        return out
    return g


def resonate(x, peaks):
    return spec(x, peak_gain(peaks))


def tv_filter(x, fc, q, loop=False, frame=2048, hop=512):
    """Time varying band-pass (STFT overlap-add). fc is a per-sample centre frequency."""
    n = len(x)
    win = np.hanning(frame)
    f = np.fft.rfftfreq(frame, 1 / SR)
    f[0] = 1e-3
    out = np.zeros(n + (0 if loop else frame))
    norm = np.zeros_like(out)
    for start in range(0, n, hop):
        idx = (np.arange(start, start + frame) % n) if loop else None
        seg = x[idx] if loop else _pad(x[start:start + frame], frame)
        c = fc[(start + frame // 2) % n]
        w = 1.0 / (q * 2.0)
        mask = np.exp(-0.5 * (np.log2(f / c) / w) ** 2)
        y = np.fft.irfft(np.fft.rfft(seg * win) * mask, frame) * win
        if loop:
            np.add.at(out, idx, y)
            np.add.at(norm, idx, win * win)
        else:
            out[start:start + frame] += y
            norm[start:start + frame] += win * win
    return (out / np.maximum(norm, 1e-3))[:n]


def _pad(seg, n):
    return seg if len(seg) == n else np.pad(seg, (0, n - len(seg)))


# --------------------------------------------------------------------- sources

def white(n, rng):
    return rng.standard_normal(n)


def pink(n, rng):
    return spec(white(n, rng), lambda f: 1.0 / np.sqrt(np.maximum(f, 10.0)))


def brown(n, rng):
    return spec(white(n, rng), lambda f: 1.0 / np.maximum(f, 15.0))


def lfo(n, rate, rng):
    """Periodic smooth random curve in 0..1 (seamless over n samples)."""
    x = spec(white(n, rng), lambda f: np.exp(-0.5 * (f / rate) ** 2))
    x -= x.min()
    return x / max(x.max(), 1e-12)


def loop_hz(hz, n):
    """Nearest frequency with a whole number of cycles in n samples."""
    return max(1, round(hz * n / SR)) * SR / n


def sweep(f0, f1, n, curve=3.0):
    """Sine gliding exponentially from f0 to f1."""
    t = np.linspace(0, 1, n)
    f = f1 + (f0 - f1) * np.exp(-curve * t * 4)
    return np.sin(TAU * np.cumsum(f) / SR)


def exp_env(n, tau, attack=0.0):
    t = secs(n)
    e = np.exp(-t / tau)
    if attack > 0:
        e = e * np.minimum(t / attack, 1.0)
    return e


def fade(x, in_s=0.002, out_s=0.01):
    x = np.array(x, float)
    a, b = int(in_s * SR), int(out_s * SR)
    env = np.ones(len(x))
    if a:
        env[:a] = np.linspace(0, 1, a)
    if b:
        env[-b:] = np.minimum(env[-b:], np.linspace(1, 0, b) ** 2)
    return x * env.reshape((len(x),) + (1,) * (x.ndim - 1))


def partials(n, freqs, amps, taus, rng=None):
    """Inharmonic decaying partials (metal, plastic, bells)."""
    t = secs(n)
    out = np.zeros(n)
    for f, a, tau in zip(freqs, amps, taus):
        phase = rng.uniform(0, TAU) if rng is not None else 0.0
        out += a * np.sin(TAU * f * t + phase) * np.exp(-t / tau)
    return out


# --------------------------------------------------------------------- mixing

def place(buf, ev, pos, gain=1.0, pan=None, loop=False):
    """Adds ev to buf at sample pos. Wraps around when loop=True. pan -1..1 for stereo buf."""
    pos = int(pos)
    ev = np.asarray(ev, float) * gain
    if buf.ndim == 2 and ev.ndim == 1:
        th = ((0.0 if pan is None else pan) + 1) * np.pi / 4
        ev = np.stack([ev * np.cos(th), ev * np.sin(th)], axis=1) * np.sqrt(2)
    n = len(buf)
    if loop:
        np.add.at(buf, (pos + np.arange(len(ev))) % n, ev)
    elif pos < n:
        end = min(n, pos + len(ev))
        buf[pos:end] += ev[:end - pos]
    return buf


def make_ir(seconds, rng, stereo=True, bright=0.5, predelay=0.012):
    """Synthetic room/hall: decaying noise whose high end dies first."""
    n = int(seconds * SR)
    t = secs(n)

    def chan():
        d = white(n, rng)
        lo = bp(d, 150, 2500, 1) * np.exp(-t / (seconds / 5.5))
        hi = bp(d, 2500, 9000, 1) * np.exp(-t / (seconds / 12.0))
        return lo + bright * hi

    ir = np.stack([chan(), chan()], axis=1) if stereo else chan()
    ir = np.roll(ir, int(predelay * SR), axis=0)
    ir[:int(predelay * SR)] = 0
    return ir / np.sqrt(np.sum(ir ** 2, axis=0, keepdims=stereo) + 1e-12)


def convolve(x, ir, loop):
    """Stereo or mono convolution. loop=True is circular (tail wraps to the start)."""
    x = np.asarray(x, float)
    if x.ndim == 1 and ir.ndim == 2:
        x = np.stack([x, x], axis=1)
    if x.ndim == 2 and ir.ndim == 1:
        ir = np.stack([ir, ir], axis=1)
    n = len(x) if loop else len(x) + len(ir)
    if loop and len(ir) > n:
        wrapped = np.zeros((n,) + ir.shape[1:])
        np.add.at(wrapped, np.arange(len(ir)) % n, ir)
        ir = wrapped
    return np.fft.irfft(np.fft.rfft(x, n, axis=0) * np.fft.rfft(ir, n, axis=0), n, axis=0)


def reverb(x, ir, wet, loop=False):
    y = convolve(x, ir, loop)
    dry = np.asarray(x, float)
    if dry.ndim == 1 and y.ndim == 2:
        dry = np.stack([dry, dry], axis=1)
    if loop:
        return dry + wet * y
    out = wet * y
    out[:len(dry)] += dry
    return out


def trim_tail(x, thresh_db=-60, min_s=0.05):
    """Cuts trailing silence of a one-shot and adds a short fade."""
    mono = np.abs(x) if x.ndim == 1 else np.abs(x).max(axis=1)
    idx = np.nonzero(mono > mono.max() * 10 ** (thresh_db / 20))[0]
    end = max(int(min_s * SR), (idx[-1] + 1) if len(idx) else len(x))
    return fade(x[:end], 0.0, min(0.02, end / SR / 4))


# --------------------------------------------------------------------- levels

def rms_db(x):
    return 20 * np.log10(np.sqrt(np.mean(np.square(x))) + 1e-12)


def peak_db(x):
    return 20 * np.log10(np.max(np.abs(x)) + 1e-12)


def active_rms_db(x):
    """RMS over 50 ms windows within 30 dB of the loudest one (one-shot loudness)."""
    w = int(0.05 * SR)
    count = max(1, len(x) // w)
    p = np.array([np.mean(x[i * w:(i + 1) * w] ** 2) for i in range(count)]) + 1e-16
    return 10 * np.log10(np.mean(p[p > p.max() * 1e-3]))


PEAK_LIMIT = 10 ** (-1.05 / 20)


def master(x, target_db):
    """Brings x to target loudness; soft-saturates peaks (memoryless, so loops stay seamless)."""
    x = np.asarray(x, float)
    x = x - x.mean(axis=0)
    knee = PEAK_LIMIT * 0.8
    room = PEAK_LIMIT - knee
    for _ in range(5):
        x = x * 10 ** ((target_db - active_rms_db(x)) / 20)
        a = np.abs(x)
        if a.max() <= PEAK_LIMIT:
            break
        x = np.where(a > knee, np.sign(x) * (knee + room * np.tanh((a - knee) / room)), x)
    return x * min(1.0, PEAK_LIMIT / np.max(np.abs(x)))


def write_wav(path, x):
    x = np.asarray(x, float)
    pcm = np.clip(np.round(x * 32767), -32768, 32767).astype('<i2')
    with wave.open(str(path), 'wb') as w:
        w.setnchannels(1 if x.ndim == 1 else x.shape[1])
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())


def read_wav(path):
    with wave.open(str(path), 'rb') as w:
        ch, sr, n = w.getnchannels(), w.getframerate(), w.getnframes()
        data = np.frombuffer(w.readframes(n), '<i2').astype(float) / 32768.0
    return (data if ch == 1 else data.reshape(-1, ch)), sr
