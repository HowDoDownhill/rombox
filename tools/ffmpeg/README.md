# FFmpeg for game streaming

RomBox runs `ffmpeg` to capture and encode a host's game, and loads FFmpeg's `avcodec` and
`avutil` libraries to decode the stream on a guest. `FfmpegTool` and `Libav` look in
`tools/ffmpeg/<os>/` next to the app first, then at what the system has installed.

The binaries are kept out of git because of their size. Each platform folder is filled by hand.

## Windows

Folder: `tools/ffmpeg/windows/`. `build.sh` copies it into the Windows build.

Use an LGPL shared build of FFmpeg 8.1 or 9.0. RomBox needs the `gfxcapture` filter (window
capture), a hardware H.264 encoder and the H.264 decoder, and loads `avcodec` versions 60 to 63.

1. Download `ffmpeg-n9.0-latest-win64-lgpl-shared-9.0.zip` from the `latest` release at
   https://github.com/BtbN/FFmpeg-Builds/releases and check it against `checksums.sha256` there.
2. Copy `bin/ffmpeg.exe`, every `bin/*.dll` and `LICENSE.txt` into `tools/ffmpeg/windows/`.
   `ffplay.exe` and `ffprobe.exe` are not used.

The folder comes to about 155 MB, 91 MB of it `avcodec-63.dll`.

FFmpeg is licensed under the LGPL. A release that includes these files has to carry
`LICENSE.txt` beside them and say where the FFmpeg source code can be had
(https://ffmpeg.org/download.html).

## Linux

Nothing is bundled. RomBox uses the distribution's `ffmpeg` and its `libavcodec` and `libavutil`.
On Arch that is the `ffmpeg` package.
