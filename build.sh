#!/bin/bash
set -e

# Godot executable. Override with: GODOT_BIN=/path/to/godot ./build.sh
if [ -z "$GODOT_BIN" ]; then
    for candidate in godot godot-mono godot4 godot4-mono; do
        if command -v "$candidate" >/dev/null 2>&1; then
            GODOT_BIN="$candidate"
            break
        fi
    done
fi

if [ -z "$GODOT_BIN" ]; then
    echo "Could not find a Godot binary on PATH. Set GODOT_BIN=/path/to/godot and try again."
    exit 1
fi

if ! command -v "$GODOT_BIN" >/dev/null 2>&1 && [ ! -x "$GODOT_BIN" ]; then
    echo "Godot not found at $GODOT_BIN. Set GODOT_BIN to the Godot 4.7.2 mono binary."
    exit 1
fi

echo "Using Godot: $GODOT_BIN"

require_assemblies() {
    if [ ! -f "$1/GodotSharp.dll" ]; then
        echo "$2 export produced no .NET assemblies in $1."
        exit 1
    fi
}

# Mirror of xcopy "SRC" "DEST" /E /I /Y - create DEST, merge into it, overwrite.
copy_tree() {
    local source_directory="$1"
    local destination_directory="$2"
    mkdir -p "$destination_directory"
    cp -r "$source_directory/." "$destination_directory/"
}

# The .NET runtime folder is regenerated on every export. Clearing it first stops stale assemblies,
# and the *.dll~RF*.TMP files Windows leaves when replacing a locked DLL, from reaching a release zip.
echo "Exporting app to Windows..."
mkdir -p build/windows
rm -rf build/windows/data_rombox_*
"$GODOT_BIN" --headless --export-release "Windows Desktop" "build/windows/rombox.exe"
require_assemblies build/windows/data_rombox_windows_x86_64 Windows

echo "Copying install_scripts and tools to Windows build..."
copy_tree "install_scripts" "build/windows/install_scripts"
rm -rf "build/windows/tools"
copy_tree "tools/7zip" "build/windows/tools/7zip"

# Game streaming on Windows runs FFmpeg from this folder. It is kept out of git because of its
# size; tools/ffmpeg/README.md says where it comes from. Linux uses the system's FFmpeg.
if [ -d "tools/ffmpeg/windows" ]; then
    copy_tree "tools/ffmpeg/windows" "build/windows/tools/ffmpeg/windows"
else
    echo "tools/ffmpeg/windows is missing; this Windows build cannot host or join a stream."
fi

echo "Exporting game to Linux..."
mkdir -p build/linux
rm -rf build/linux/data_rombox_*
"$GODOT_BIN" --headless --export-release "Linux Desktop" "build/linux/rombox.x86_64"
require_assemblies build/linux/data_rombox_linuxbsd_x86_64 Linux

echo "Copying install_scripts and tools to Linux build..."
copy_tree "install_scripts" "build/linux/install_scripts"
rm -rf "build/linux/tools"
copy_tree "tools/7zip" "build/linux/tools/7zip"

chmod +x "build/linux/rombox.x86_64" "build/linux/tools/7zip/linux/7zz"

# The macOS app keeps its data in Application Support, so install_scripts travels inside the
# package as one zip. Godot skips folders holding a .gdignore, which install_scripts has.
case "$(uname -s)" in
    MINGW*|MSYS*|CYGWIN*) SEVEN_ZIP="$PWD/tools/7zip/windows/7za.exe" ;;
    Darwin*)              SEVEN_ZIP="$PWD/tools/7zip/macOS/7zz" ;;
    *)                    SEVEN_ZIP="$PWD/tools/7zip/linux/7zz" ;;
esac
chmod +x "$SEVEN_ZIP" 2>/dev/null || true

echo "Packing install_scripts for macOS..."
rm -rf bundled
mkdir -p bundled
(cd install_scripts && "$SEVEN_ZIP" a -tzip "../bundled/install_scripts.zip" '*' '-xr!.gdignore' >/dev/null)

echo "Exporting app to macOS..."
mkdir -p build/macos
rm -f build/macos/rombox-macos.zip
"$GODOT_BIN" --headless --export-release "macOS" "build/macos/rombox-macos.zip"
if [ ! -f build/macos/rombox-macos.zip ]; then
    echo "macOS export produced no zip."
    exit 1
fi

echo "Build complete!"
