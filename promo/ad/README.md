# The 15-second ad

A 1080p60 clip with its soundtrack, generated entirely from code: numpy synthesizes the music, moderngl shades the picture, and skia draws the type and the node editor. Picture and sound read one event list in `common.py`, so every cut lands on a hit.

Needs Python 3.11 (moderngl has no wheels for newer versions yet), an OpenGL 3.3 GPU, and ffmpeg on `PATH`. The fonts, Inter and JetBrains Mono, are fetched from google/fonts on the first run.

```bash
py -3.11 -m pip install -r promo/ad/requirements.txt
py -3.11 promo/ad/video.py
```

That writes `promo/ad/flyback_ad.mp4`, building `music.wav` first if it is missing. After a change to the score, run `music.py` again.

| File | What it holds |
|---|---|
| `common.py` | The timeline: tempo, scene boundaries, hits, chords, easing |
| `music.py` | The score and every instrument, mixed and leveled to -13 LUFS |
| `shaders.py` | The seven drop visuals, phosphor feedback, bloom and the tube |
| `overlay.py` | Type, modules, wires, the mark and the HUD |
| `video.py` | The render loop, piped to ffmpeg |
| `sheet.py` | Tiles `still_*.png` into `sheet.png` for review |

To check a moment without rendering the film, pass seconds: `video.py 1.2 7.8 11.4` writes `still_01.20.png` and the rest, and `sheet.py` puts them on one page.
