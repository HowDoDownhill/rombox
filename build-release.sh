#!/bin/bash
set -e

echo "Building Release Packages..."

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

./build.sh

echo
echo "Preparing release folder..."
mkdir -p releases

case "$(uname -s)" in
    MINGW*|MSYS*|CYGWIN*) SEVEN_ZIP="$SCRIPT_DIR/tools/7zip/windows/7za.exe" ;;
    Darwin*)              SEVEN_ZIP="$SCRIPT_DIR/tools/7zip/macOS/7zz" ;;
    *)                    SEVEN_ZIP="$SCRIPT_DIR/tools/7zip/linux/7zz" ;;
esac
chmod +x "$SEVEN_ZIP" 2>/dev/null || true

# Only the files listed below are packaged. The build folders also accumulate
# runtime data whenever the app is run in place for testing - config.cfg with
# live credentials, plus roms, bios, downloads, saves and caches - and zipping
# the folder wholesale shipped all of it. Keep this list in step with
# build-release.bat, and add new shipped files here rather than using a wildcard.

echo
echo "Zipping Windows Release..."
rm -f releases/rombox-windows.zip
(cd build/windows && "$SEVEN_ZIP" a -tzip "$SCRIPT_DIR/releases/rombox-windows.zip" \
    rombox.exe rombox.console.exe rombox.pck data_rombox_* install_scripts tools)

echo
echo "Zipping Linux Release..."
rm -f releases/rombox-linux.zip
(cd build/linux && "$SEVEN_ZIP" a -tzip "$SCRIPT_DIR/releases/rombox-linux.zip" \
    rombox.x86_64 rombox.sh rombox.pck data_rombox_* install_scripts tools)

# Installs from 1.2.2 and earlier only download a zip named romm-frontend-<platform>.zip and
# restart romm-frontend.exe after extracting it. These bridge zips carry the same build with the
# executable and .pck under the old names; the app moves itself to the new names on first start.
echo
echo "Making bridge zips for installs from 1.2.2 and earlier..."
cp -f releases/rombox-windows.zip releases/romm-frontend-windows.zip
"$SEVEN_ZIP" rn releases/romm-frontend-windows.zip rombox.exe romm-frontend.exe rombox.console.exe romm-frontend.console.exe rombox.pck romm-frontend.pck
cp -f releases/rombox-linux.zip releases/romm-frontend-linux.zip
"$SEVEN_ZIP" rn releases/romm-frontend-linux.zip rombox.x86_64 romm-frontend.x86_64 rombox.pck romm-frontend.pck

echo
echo "Copying macOS Release..."
cp -f build/macos/rombox-macos.zip releases/rombox-macos.zip

echo
echo "Release packages created successfully in the 'releases' folder!"
echo "You can now upload 'rombox-windows.zip', 'rombox-linux.zip', 'rombox-macos.zip',"
echo "'romm-frontend-windows.zip' and 'romm-frontend-linux.zip' to GitHub Releases."
