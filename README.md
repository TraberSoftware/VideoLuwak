<p align="center">
  <img src="docs/images/logo.png" width="96" alt="VideoLuwak logo" />
</p>

<h1 align="center">VideoLuwak</h1>

<p align="center">
  <strong>Turn any presentation video into clean audio and just the slides that matter — ready for an LLM.</strong>
</p>

<p align="center">
  <a href="#releases-github-actions"><img alt="Version 1.0.0" src="https://img.shields.io/badge/version-1.0.0-2F8F83?style=flat-square"></a>
  <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10-512BD4?style=flat-square&logo=dotnet&logoColor=white">
  <img alt="Windows, Linux, macOS" src="https://img.shields.io/badge/platforms-Windows%20%7C%20Linux%20%7C%20macOS-2F8F83?style=flat-square">
  <a href="LICENSE"><img alt="License: MIT" src="https://img.shields.io/badge/license-MIT-2F8F83?style=flat-square"></a>
</p>

<p align="center">
  <a href="#screenshots">Screenshots</a> ·
  <a href="#quick-start">Quick start</a> ·
  <a href="#how-it-works">How it works</a> ·
  <a href="#releases-github-actions">Releases</a>
</p>

<p align="center">
  <img src="docs/images/screenshot-results.png" width="100%" alt="VideoLuwak after processing a 13-minute Microsoft Teams tutorial: 30.7% saved, one 16 kHz mono audio stream, and 127 of 195 keyframes kept." />
</p>

VideoLuwak is a small, portable **.NET 10** desktop and command-line utility that pulls the useful
content out of a video using **ffmpeg** — every audio stream as a separate WAV, every keyframe as an
image, and a perceptually de-duplicated copy of those keyframes — then reports how much smaller the
result is.

It is built for one job: **turn a presentation recording into a clean bundle for an LLM.** The audio
is ready for speech-to-text, and the kept frames are the visual content worth sending to a multimodal
model.

### Why VideoLuwak?

- **Audio that transcription models expect.** Every stream becomes a 16-bit PCM WAV, downmixed to
  mono and resampled to 16 kHz by default (Whisper, Parakeet, wav2vec).
- **Fewer frames, not fewer ideas.** A dependency-free 64-bit perceptual hash removes near-identical
  keyframes while preserving chronological order.
- **Inspectable by design.** Every run leaves a machine-readable summary, a per-stream audio
  manifest, and ordinary files you can open, copy, or feed to any downstream tool.
- **Two ways to use it.** Drop files into the desktop app, or script the same engine from the CLI.

## Screenshots

### Queue and live processing

<table>
  <tr>
    <td width="50%">
      <img src="docs/images/screenshot-queue.png" width="100%" alt="VideoLuwak queue with a 1080p Microsoft Teams tutorial ready to process" />
    </td>
    <td width="50%">
      <img src="docs/images/screenshot-processing.png" width="100%" alt="VideoLuwak processing a video with Audio, Keyframes, and Dedupe stages" />
    </td>
  </tr>
  <tr>
    <td align="center"><sub><b>Queue</b> — drop files, see size, status, and per-file progress.</sub></td>
    <td align="center"><sub><b>Processing</b> — follow the Audio → Keyframes → Dedupe pipeline.</sub></td>
  </tr>
</table>

### Results

The result card is the one-glance answer: how much smaller the output is, which audio streams were
produced, and how many keyframes survived de-duplication.

<table>
  <tr>
    <td width="58%" valign="middle">
      <img src="docs/images/results-closeup.png" width="100%" alt="VideoLuwak results card showing 30.7% saved and 127 of 195 keyframes kept" />
    </td>
    <td width="42%" valign="middle">
      <h4>What the numbers mean</h4>
      <ul>
        <li><b>30.7% smaller</b> — the extracted bundle versus the source video.</li>
        <li><b>One audio stream</b> — 16 kHz mono PCM, tagged <code>STT</code>.</li>
        <li><b>127 of 195 keyframes kept</b> — 68 near-duplicates removed.</li>
        <li><b>Open output</b> — jump straight to <code>audio/</code>, <code>final_keyframes/</code>, and the JSON summary.</li>
      </ul>
    </td>
  </tr>
</table>

### Settings

<img src="docs/images/screenshot-settings.png" width="420" alt="VideoLuwak settings for output, keyframes, and speech-to-text audio" />

## Quick start

1. **Download a release.** Grab the latest Windows, Linux, or macOS build from
   [Releases](#releases-github-actions). The Windows build is portable and embeds ffmpeg; Linux and
   macOS use the system `ffmpeg`/`ffprobe`.
2. **Add a video.** Drop it onto the VideoLuwak window, or run the CLI:
   `VideoLuwak "presentation.mp4" -o ./extracts`.
3. **Use the result.** `audio/` contains one WAV per stream, `final_keyframes/` contains the
   de-duplicated frames, and `processing_result.json` + `audio_streams.json` describe the run.

---

## How it works

For each input video, VideoLuwak creates `<output base>/<video name>/` containing:

```
<video name>/
├── audio/                     # one .wav per audio stream + a manifest
│   ├── audio_01_eng.wav
│   ├── audio_02_spa_Commentary.wav
│   └── audio_streams.json     # source + output format (sample rate / channels / sample format / STT) per stream
├── keyframes/                 # every true keyframe (I-frame) extracted from the video
│   ├── kf_000001.jpg
│   └── ...
├── final_keyframes/           # keyframes kept after perceptual-hash de-duplication
│   └── ...
└── processing_result.json     # machine-readable summary of the run
```

- **Audio** — every audio stream is probed with `ffprobe` and extracted individually to a
  `pcm_s16le` (16-bit PCM) WAV. By default each WAV is **optimized for speech-to-text** — downmixed
  to **mono** and resampled to **16 kHz** — exactly what Whisper, Parakeet/NVIDIA NeMo and wav2vec
  expect, so no extra conversion step is needed before transcription. This behavior is controlled by
  `ProcessingOptions.SttOptimizeAudio` (default `true`) plus `AudioSampleRate`, `AudioChannels`,
  `AudioHighPassHz` and `AudioNormalizeLoudness`, or via the CLI flags below. When STT optimization
  is disabled the source channel layout and sample rate are preserved (still 16-bit PCM WAV).
  `audio_streams.json` describes the source and the produced output for each stream so you can
  decide which one to keep. Each entry carries the source properties plus the output
  `outputSampleRate`, `outputChannels`, `outputSampleFormat`, `sttOptimized` and a human-readable
  `outputDescription`; the document also has a top-level `sttOptimized` flag and a `target` object
  with the `sampleRate`, `channels` and `sampleFormat` that were applied.
- **Keyframes** — extracted with `ffmpeg -skip_frame nokey` (genuine I-frames only). Files are
  zero-padded (`kf_%06d`) so alphabetical order equals chronological order.
- **De-duplication** — keyframes are enumerated in ascending order; each frame is compared to the
  **previous frame's 64-bit perceptual hash (pHash)**. If the Hamming distance is `<= threshold`
  the frame is dropped. The reference hash **always rolls forward** to the last generated frame —
  even when that frame was removed — so a chain of near-identical frames cannot "drift" past the
  threshold and all collapse to the first one.
- **Summary** — `ProcessingResult` (and `processing_result.json`) report original size, extracted
  audio, keyframes extracted/kept/removed, total output size, and the size reduction percentage.

### pHash without an image library

To stay dependency-light and license-clean, the perceptual hash does not use an image library:
ffmpeg decodes each keyframe to a raw **32×32 grayscale** frame, and a pure C# DCT-II implementation
(`PerceptualHash64`) computes the 64-bit hash (top-left 8×8 DCT coefficients, DC excluded,
median-thresholded).

---

## ffmpeg provisioning

VideoLuwak resolves `ffmpeg`/`ffprobe` in this order (first hit wins):

1. An explicit path (CLI `--ffmpeg` / `--ffprobe`).
2. `VIDEOLUWAK_FFMPEG` / `VIDEOLUWAK_FFPROBE` environment variables.
3. Next to the executable (or in a `tools/` subfolder).
4. **Embedded binaries** (Windows release builds only).
5. The system `PATH`.

- **Windows** — the portable build embeds static `ffmpeg.exe`/`ffprobe.exe` inside the executable
  and extracts them to a per-user cache on first run, so it runs with **no external installation**.
- **Linux/macOS** — the app uses the **system** ffmpeg/ffprobe from `PATH` (nothing is embedded).

To embed the Windows binaries before publishing, fetch them once:

```powershell
pwsh ./scripts/fetch-ffmpeg.ps1
```

This downloads a pinned ffmpeg 8.1.2 static Windows build into `assets/ffmpeg/win-x64/`
(git-ignored), verifies the archive SHA-256, and the app project embeds the binaries automatically
when present.

---

## Build & run

Requires the **.NET 10 SDK**.

```bash
# Build everything (resolves VideoLuwak.slnx)
dotnet build

# Run the CLI
dotnet run --project src/VideoLuwak.App -- "movie.mkv"

# Run the GUI
dotnet run --project src/VideoLuwak.App
```

### Publishing

```powershell
# Windows, single-file portable exe (embeds ffmpeg — run fetch-ffmpeg.ps1 first)
dotnet publish src/VideoLuwak.App -c Release -r win-x64 --self-contained true ^
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

```bash
# Linux/macOS (uses the system ffmpeg)
dotnet publish src/VideoLuwak.App -c Release -r linux-x64 --self-contained true \
    -p:PublishSingleFile=true
```

### Releases (GitHub Actions)

The application version is defined **once** in `Directory.Build.props`:

```xml
<Version>1.0.0</Version>
```

That value drives `VideoLuwak --version`, every published assembly, the artifact names, and the
release tag. Bump it before merging a release change into `master`.

`.github/workflows/release.yml` runs only on pushes to `master`:

1. Reads `<Version>` from `Directory.Build.props`.
2. **Blocks the release if the tag `v<Version>` already exists** — no artifacts are built and no
   release is created. Re-pushing `master` without a version bump is therefore a no-op.
3. Runs the test suite with ffmpeg installed.
4. Builds and packages:

   | Platform | Artifact |
   | --- | --- |
   | Windows x64 | `VideoLuwak-<Version>-win-x64.zip` — portable single-file exe with ffmpeg embedded |
   | Linux x64 | `VideoLuwak-<Version>-linux-x64.tar.gz` — self-contained binary (uses system ffmpeg) |
   | macOS Apple Silicon | `VideoLuwak-<Version>-macos-arm64.dmg` — `.app` bundle (uses system ffmpeg) |
   | macOS Intel | `VideoLuwak-<Version>-macos-x64.dmg` — `.app` bundle (uses system ffmpeg) |

5. Creates the `v<Version>` tag at the built commit and a GitHub release. The release notes list
   **every commit since the previous `v*` tag**, with links to each commit and its author. The
   Windows job also smoke-tests the published exe with ffmpeg removed from `PATH`, proving the
   portable build really uses its embedded binaries.

To publish a new version, bump the manifest and push to `master`:

```bash
# Edit Directory.Build.props: <Version>1.0.1</Version>
git add Directory.Build.props
git commit -m "Release 1.0.1"
git push origin master
```

The version badge at the top of this README is static so it renders without JavaScript; update
`version-1.0.x` in the badge URL alongside the manifest bump.

macOS builds are ad-hoc signed but not notarized; the first launch may require
right-click → **Open**. Linux and macOS builds require `ffmpeg`/`ffprobe` on `PATH`; the Windows
portable build embeds them.

---

## Command-line usage

```
VideoLuwak [options] <video> [<video> ...]

Options:
  -o, --output <dir>     Base output directory (default: alongside each video)
  -t, --threshold <n>    pHash Hamming-distance threshold for duplicate frames (default 8)
      --format <jpg|png> Keyframe image format (default jpg)
      --quality <1-31>   JPEG quality when --format jpg (default 2, lower = better)
      --no-dedup         Skip the final de-duplicated keyframes folder
      --stt-audio        Downmix/resample audio for speech-to-text (default)
      --no-stt-audio     Keep the source channel layout and sample rate
      --audio-rate <hz>  STT target sample rate (default 16000)
      --audio-channels <n> STT target channel count, >= 1 (default 1)
      --mono             Shorthand for --audio-channels 1
      --stereo           Shorthand for --audio-channels 2
      --highpass <hz>    High-pass filter cutoff, >= 0; 0 disables (default 0)
      --loudnorm         Apply EBU R128 loudness normalization (default off)
      --no-loudnorm      Disable loudness normalization (default)
      --ffmpeg <path>    Explicit ffmpeg executable
      --ffprobe <path>   Explicit ffprobe executable
  -h, --help             Show help
      --version          Show version
      --gui              Force the graphical interface
```

By default extracted audio is mono 16 kHz 16-bit PCM (what STT models want); use
`--no-stt-audio` to keep the source layout, or the flags above to tune the output.

Examples:

```bash
VideoLuwak movie.mkv
VideoLuwak movie.mkv -o /data/extracts --threshold 6
VideoLuwak a.mp4 b.mkv --format png
VideoLuwak movie.mkv --audio-rate 16000 --highpass 80
```

Exit codes: `0` success, `1` a file failed / ffmpeg missing, `2` usage error.

Double-clicking the application (no arguments) opens the GUI instead.

---

## GUI

The desktop app is organized as a two-pane batch workspace:

- **Header** — the global output folder is a clickable path chip; the gear opens the dedicated
  settings view.
- **Queue** — a compact list with per-file status, live stage/progress, and inline errors. Drop
  files anywhere on the window, use *Add files*, or use the drop strip when the queue is full.
  Queued files can be removed individually; *Clear* empties the queue.
- **Run bar** — one status line, an Audio → Keyframes → Dedupe stepper, batch and per-file
  progress, and state-aware actions: *Process*, *Cancel*, *Open output*, *Run again*.
- **Results** — a single aggregate card (percent smaller, bytes saved, before/after, kept vs.
  removed bar, completed/failed counts) followed by expandable per-file rows. A file row shows
  audio streams, the keyframe kept ratio, and open/copy actions; failed rows show the error and
  diagnostics. Savings are computed from successful files only, so a failed file can never
  inflate the reduction.
- **Settings view** — a dedicated window (gear icon) with output destination, keyframe format,
  JPEG quality, pHash similarity threshold, dedupe, and the audio-for-speech-to-text group
  (sample rate, mono/stereo, high-pass, EBU R128 normalization). Changes are saved automatically
  and restored on the next launch.
- **Status line** — the bottom of the queue reports add/shell errors and run status.

Settings live at:

- Windows: `%APPDATA%\VideoLuwak\settings.json`
- Linux/macOS: `$XDG_CONFIG_HOME/VideoLuwak/settings.json` (or `~/.config/VideoLuwak/settings.json`)

### Brand assets

The logo lives in `src/VideoLuwak.App/Assets/logo/`:

- `logo-1024.png` — master logo (1024×1024).
- `logo-512.png` … `logo-16.png` — full-logo exports.
- `logo-mark-transparent-512.png` … `-16.png` — cropped paw mark with transparency, used in the header and empty state.
- `logo-mark-square-1024.png` … `-16.png` — tight square app-icon exports.
- `logo.ico` — multi-size Windows icon (16, 20, 24, 32, 40, 48, 64, 96, 128, 256),
  with 32-bit DIB entries for small sizes and PNG entries for large sizes.

The executable icon is embedded via `ApplicationIcon`; on Windows the window/taskbar icon
uses the multi-size ICO so the shell can pick the exact size. Other platforms use the
square PNG icon.

---

## Testing

```bash
dotnet test
```

Unit tests cover the pHash, the de-duplication rolling-reference algorithm, path layout, CLI
parsing, progress parsing and settings. Integration tests generate a real multi-audio video with
ffmpeg and run the full pipeline end-to-end (they no-op when ffmpeg is not installed).

---

## License

VideoLuwak is released under the **[MIT License](LICENSE)**.

The Windows portable build embeds ffmpeg/ffprobe, which are distributed under the **GNU General
Public License v3.0** (`--enable-gpl --enable-version3`). See
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) and
[LICENSES/GPL-3.0.txt](LICENSES/GPL-3.0.txt) for the ffmpeg source/license details and the other
third-party components. Linux and macOS builds use the system ffmpeg instead of embedding it.
Published archives include `LICENSE`, `THIRD_PARTY_NOTICES.md`, and the `LICENSES/` folder.

---

## Project layout

```
src/VideoLuwak.Core   # cross-platform engine: ffmpeg, hashing, pipeline, settings, CLI
src/VideoLuwak.App    # Avalonia app: dual-mode entry point (CLI + GUI)
tests/                # unit + integration tests
scripts/              # ffmpeg asset fetcher + macOS DMG packager
assets/ffmpeg/win-x64 # (fetched, git-ignored) embedded Windows binaries
.github/workflows     # release automation (master -> test -> build -> GitHub release)
docs/                 # README screenshots
LICENSES/             # third-party license texts
```
