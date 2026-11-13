# MsQuic for browser guests

A guest in a web browser connects with WebTransport, which runs over QUIC. .NET's QUIC support is
a thin layer over Microsoft's MsQuic library. On Windows the library comes with .NET. On Linux it
is a separate install that most systems lack, so RomBox ships it.

The binary is kept out of git. The platform folder is filled by hand.

## Linux

Folder: `tools/msquic/linux/`. `build.sh`, `build.bat` and `tools/linux-test/deploy.ps1` copy
`libmsquic.so.2` into the Linux build's `data_rombox_linuxbsd_x86_64` folder, beside
`System.Net.Quic.dll`, which is where .NET looks for it first.

1. Download `libmsquic_2.4.20_amd64.deb` from
   https://packages.microsoft.com/ubuntu/22.04/prod/pool/main/libm/libmsquic/ and check its
   SHA-256 against the `Packages` index of that repository
   (`88fd1e3f3d41b64d848e1a1324098f455378b364581eebe3e941f8fd6057cecc`).
2. Take `usr/lib/x86_64-linux-gnu/libmsquic.so.2.4.20` out of the package and save it as
   `tools/msquic/linux/libmsquic.so.2`. The `lttng` variant in the package is for tracing and
   is not used.
3. Put MsQuic's `LICENSE` and `THIRD-PARTY-NOTICES` beside it as `LICENSE.txt` and
   `THIRD-PARTY-NOTICES.txt`. Both are in the MsQuic repository and in its NuGet packages.

Use a 2.4 release: that is the line .NET 8 is tested with.

The library needs `libcrypto.so.3` (OpenSSL 3) and `libnuma.so.1` from the system. Without them
it does not load, RomBox logs that browsers cannot join, and everything else works as before.

MsQuic is under the MIT license. A release that includes it carries the two text files in
`tools/msquic/linux/`.
