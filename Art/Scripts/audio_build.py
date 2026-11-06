"""Builds every Luge Rush sound into Game/Assets/_LugeRush/Audio, then measures them.

    python Art/Scripts/audio_build.py            # build + verify + spectrograms
    python Art/Scripts/audio_build.py --only ui  # one group (sled, track, ambience, music, ui)
    python Art/Scripts/audio_build.py --verify   # measure existing files only

Needs numpy; matplotlib only for the spectrogram sheets. No external samples are used.
"""
import pathlib
import sys
import time

import numpy as np

sys.path.insert(0, str(pathlib.Path(__file__).parent))
from audio_dsp import SR, active_rms_db, master, peak_db, read_wav, rms_db, write_wav  # noqa: E402
import audio_ambience as amb  # noqa: E402
import audio_music as mus  # noqa: E402
import audio_sfx as sfx  # noqa: E402

ROOT = pathlib.Path(__file__).resolve().parents[2]
OUT = ROOT / 'Game/Assets/_LugeRush/Audio'
RENDERS = ROOT / 'Art/Renders'

# group, file, builder, loudness target (dB, 50 ms windowed RMS), loop?
SOUNDS = [
    ('Sled', 'sled_roll', sfx.sled_roll, -21, True),
    ('Sled', 'sled_wind', sfx.sled_wind, -22, True),
    ('Sled', 'sled_brake', sfx.sled_brake, -21, True),
    ('Sled', 'sled_drift', sfx.sled_drift, -21, True),
    ('Sled', 'sled_scrape', sfx.sled_scrape, -20, True),
    ('Sled', 'sled_boost', sfx.sled_boost, -15, False),
    ('Sled', 'sled_impact_light', lambda: sfx.impact('impact_light', 0.1), -16, False),
    ('Sled', 'sled_impact_medium', lambda: sfx.impact('impact_medium', 0.5), -15, False),
    ('Sled', 'sled_impact_heavy', lambda: sfx.impact('impact_heavy', 1.0), -14, False),
    ('Track', 'lift_chain', sfx.lift_chain, -21, True),
    ('Track', 'race_beep', sfx.race_beep, -17, False),
    ('Track', 'race_go', sfx.race_go, -16, False),
    ('Track', 'lap_complete', sfx.lap_complete, -18, False),
    ('Track', 'lap_final', sfx.lap_final, -17, False),
    ('Track', 'race_finish', sfx.race_finish, -17, False),
    ('Ambience', 'amb_forest', amb.forest, -26, True),
    ('Ambience', 'amb_alpine', amb.alpine, -26, True),
    ('Ambience', 'amb_canyon', amb.canyon, -26, True),
    ('Music', 'music_menu', mus.music_menu, -17, True),
    ('Music', 'music_race', mus.music_race, -16, True),
    ('UI', 'ui_nav', sfx.ui_nav, -22, False),
    ('UI', 'ui_confirm', sfx.ui_confirm, -21, False),
    ('UI', 'ui_back', sfx.ui_back, -22, False),
    ('UI', 'ui_notify', sfx.ui_notify, -21, False),
]


def build(only):
    for group, name, fn, target, _ in SOUNDS:
        if only and group.lower() != only:
            continue
        t0 = time.time()
        (OUT / group).mkdir(parents=True, exist_ok=True)
        x = master(fn(), target)
        write_wav(OUT / group / f'{name}.wav', x)
        print(f'  {group}/{name}: {len(x) / SR:.2f} s in {time.time() - t0:.1f} s', flush=True)


def seam_ratio(x):
    """Jump across the loop point relative to the 99.9th percentile of ordinary sample steps."""
    m = x if x.ndim == 1 else x.T
    steps = np.abs(np.diff(m))
    limit = np.percentile(steps, 99.9, axis=-1)
    jump = np.abs(np.asarray(m)[..., 0] - np.asarray(m)[..., -1])
    return float(np.max(jump / (limit + 1e-9)))


def verify():
    rows, fails, total = [], 0, 0
    print(f"{'file':<22}{'ch':>3}{'sec':>7}{'KB':>7}{'peak':>7}{'rms':>7}{'loud':>7}{'clip':>6}{'seam':>7}  status")
    for group, name, _, target, loop in SOUNDS:
        path = OUT / group / f'{name}.wav'
        x, sr = read_wav(path)
        kb = path.stat().st_size / 1024
        total += kb
        clipped = int(np.sum(np.abs(x) >= 0.9999))
        seam = seam_ratio(x) if loop else 0.0
        ok = sr == 44100 and peak_db(x) <= -1.0 and clipped == 0 and seam <= 1.0 \
            and abs(active_rms_db(x) - target) <= 1.5
        if not loop:
            edge = max(abs(float(np.max(np.abs(x[-1])))), 0)
            ok = ok and edge < 0.01
        fails += not ok
        ch = 1 if x.ndim == 1 else x.shape[1]
        line = f'{name:<22}{ch:>3}{len(x) / sr:>7.2f}{kb:>7.0f}{peak_db(x):>7.2f}{rms_db(x):>7.1f}' \
               f'{active_rms_db(x):>7.1f}{clipped:>6}{seam:>7.2f}  {"ok" if ok else "FAIL"}'
        print(line)
        rows.append(line)
    summary = f'{len(SOUNDS)} files, {total / 1024:.1f} MB WAV, {fails} failed'
    print(summary)
    RENDERS.mkdir(parents=True, exist_ok=True)
    (RENDERS / 'audio_report.txt').write_text('\n'.join(rows + [summary]) + '\n', encoding='utf-8')
    return fails


def spectrograms():
    try:
        import matplotlib
        matplotlib.use('Agg')
        import matplotlib.pyplot as plt
    except ImportError:
        print('matplotlib missing: spectrograms skipped')
        return
    for group in dict.fromkeys(g for g, *_ in SOUNDS):
        items = [s for s in SOUNDS if s[0] == group]
        cols = 3 if len(items) > 4 else 2
        rows = int(np.ceil(len(items) / cols))
        fig, axes = plt.subplots(rows, cols, figsize=(5.2 * cols, 2.5 * rows), squeeze=False)
        for ax in axes.flat[len(items):]:
            ax.axis('off')
        for ax, (_, name, _, _, _) in zip(axes.flat, items):
            x, _ = read_wav(OUT / group / f'{name}.wav')
            mono = x if x.ndim == 1 else x.mean(axis=1)
            nfft = 2048
            hop = max(128, len(mono) // 1400)
            frames = np.lib.stride_tricks.sliding_window_view(np.pad(mono, (0, nfft)), nfft)[::hop]
            s = 20 * np.log10(np.abs(np.fft.rfft(frames * np.hanning(nfft), axis=1)).T + 1e-5)
            f = np.fft.rfftfreq(nfft, 1 / SR)
            lo = 40
            keep = f >= lo
            t = np.arange(s.shape[1]) * hop / SR
            ax.pcolormesh(t, f[keep], s[keep], shading='auto', cmap='magma', vmin=-50, vmax=45, rasterized=True)
            ax.set_yscale('log')
            ax.set_ylim(lo, 20000)
            ax.set_title(name, fontsize=9)
            ax.tick_params(labelsize=7)
        fig.tight_layout()
        fig.savefig(RENDERS / f'audio_{group.lower()}.png', dpi=80)
        plt.close(fig)
        print(f'  audio_{group.lower()}.png')


def main():
    args = sys.argv[1:]
    only = args[args.index('--only') + 1].lower() if '--only' in args else None
    if '--verify' not in args:
        build(only)
    fails = verify()
    spectrograms()
    sys.exit(1 if fails else 0)


if __name__ == '__main__':
    main()
