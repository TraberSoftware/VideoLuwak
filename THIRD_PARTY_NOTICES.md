# Third-Party Notices

VideoLuwak is released under the [MIT License](LICENSE). It depends on the following third-party
software. Test-only packages are not included in the published binaries.

## ffmpeg and ffprobe (Windows portable build)

The Windows portable build embeds `ffmpeg.exe` and `ffprobe.exe` as independent executables. They
are invoked as child processes by VideoLuwak and are not linked into the application.

The binaries are downloaded by `scripts/fetch-ffmpeg.ps1` from the
[gyan.dev FFmpeg builds](https://www.gyan.dev/ffmpeg/builds/) and are built with
`--enable-gpl --enable-version3`. They are therefore distributed under the
**GNU General Public License, version 3 (GPLv3)**.

The script pins `ffmpeg-8.1.2-essentials_build.zip` and verifies its SHA-256 before extracting the
binaries.

- FFmpeg project: <https://ffmpeg.org/>
- FFmpeg source code: <https://github.com/FFmpeg/FFmpeg>
- Windows build information: <https://www.gyan.dev/ffmpeg/builds/>
- A copy of the GPLv3 text is included at [LICENSES/GPL-3.0.txt](LICENSES/GPL-3.0.txt).

If you redistribute a VideoLuwak build that contains these binaries, you are responsible for
complying with the GPLv3, including making the corresponding source code available. The fetch
script verifies the pinned archive but does not download the corresponding source archive; include
the source (or a written offer) with public release assets.

Linux and macOS builds do not embed ffmpeg; they use the system `ffmpeg`/`ffprobe` installed by the
user.

## Runtime dependencies

| Component | License | Project |
| --- | --- | --- |
| Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent, Avalonia.Fonts.Inter | MIT | <https://github.com/AvaloniaUI/Avalonia> |
| Inter font | SIL Open Font License 1.1 | <https://github.com/rsms/inter> |
| CommunityToolkit.Mvvm | MIT | <https://github.com/CommunityToolkit/dotnet> |
| SkiaSharp / HarfBuzzSharp | MIT | <https://github.com/mono/SkiaSharp> |
| .NET runtime | MIT | <https://github.com/dotnet/runtime> |

## Test-only dependencies

The test project uses FluentAssertions, xUnit, coverlet, and the Microsoft .NET Test SDK. These
packages are used only to build and run the test suite and are not part of the published Windows,
Linux, or macOS artifacts.
