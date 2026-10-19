# Third-Party Notices

VideoLuwak is released under the [MIT License](LICENSE). It depends on the following third-party
software. Test-only packages are not included in the published binaries.

## FFmpeg and FFprobe (system dependency, not bundled)

VideoLuwak does not distribute FFmpeg or FFprobe binaries. Both are required system dependencies and
are resolved from an explicit path, the `VIDEOLUWAK_FFMPEG` / `VIDEOLUWAK_FFPROBE` environment
variables, the application directory, or `PATH`.

Windows first-run setup can install FFmpeg automatically with `winget` (the
`Gyan.FFmpeg.Essentials` package). That installation is performed by the user's package manager;
VideoLuwak does not redistribute the downloaded binaries. Linux and macOS users install FFmpeg with
their system package manager.

FFmpeg is licensed under the GNU Lesser General Public License v2.1 or later by default, but builds
that include GPL-covered optional parts are licensed under the GPL. The exact terms depend on the
build installed on the user's system. See the official FFmpeg legal page:
<https://ffmpeg.org/legal.html>.

FFmpeg is a trademark of Fabrice Bellard. VideoLuwak is not endorsed by or affiliated with the
FFmpeg project.

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

## Website icon assets

The static site in `site/` embeds the following open-source icon sets as inline SVG. They are served
with the website only and are not part of the published application binaries.

| Component | License | Project |
| --- | --- | --- |
| Lucide icons | ISC | <https://lucide.dev> |
| Material Design Icons (brand icons) | Apache 2.0 | <https://pictogrammers.com/library/mdi/> |

Lucide is © Lucide Contributors. Material Design Icons are © Pictogrammers. License texts:
[Lucide ISC](https://github.com/lucide-icons/lucide/blob/main/LICENSE) and
[Material Design Icons Apache 2.0](https://github.com/Templarian/MaterialDesign/blob/master/LICENSE).
