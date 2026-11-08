@echo off
cd /d "%~dp0"
echo Building Release Packages...

:: Run the existing build script
call "%~dp0build.bat" nopause
if errorlevel 1 (
    echo Build failed; no release packages were made.
    pause
    exit /b 1
)

echo.
echo Preparing release folder...
if not exist releases mkdir releases

:: Only the files listed below are packaged. The build folders also accumulate
:: runtime data whenever the app is run in place for testing - config.cfg with
:: live credentials, plus roms, bios, downloads, saves and caches - and zipping
:: the folder wholesale shipped all of it. Add new shipped files here rather
:: than switching back to a wildcard.

echo.
echo Zipping Windows Release...
if exist releases\rombox-windows.zip del releases\rombox-windows.zip
pushd build\windows
..\..\tools\7zip\windows\7za.exe a -tzip "..\..\releases\rombox-windows.zip" rombox.exe rombox.console.exe rombox.pck data_rombox_* install_scripts tools
popd

echo.
echo Zipping Linux Release...
if exist releases\rombox-linux.zip del releases\rombox-linux.zip
pushd build\linux
..\..\tools\7zip\windows\7za.exe a -tzip "..\..\releases\rombox-linux.zip" rombox.x86_64 rombox.sh rombox.pck data_rombox_* install_scripts tools
popd

:: Installs from 1.2.2 and earlier only download a zip named romm-frontend-<platform>.zip and
:: restart romm-frontend.exe after extracting it. These bridge zips carry the same build with the
:: executable and .pck under the old names; the app moves itself to the new names on first start.
echo.
echo Making bridge zips for installs from 1.2.2 and earlier...
copy /y "releases\rombox-windows.zip" "releases\romm-frontend-windows.zip" >nul
tools\7zip\windows\7za.exe rn "releases\romm-frontend-windows.zip" rombox.exe romm-frontend.exe rombox.console.exe romm-frontend.console.exe rombox.pck romm-frontend.pck
copy /y "releases\rombox-linux.zip" "releases\romm-frontend-linux.zip" >nul
tools\7zip\windows\7za.exe rn "releases\romm-frontend-linux.zip" rombox.x86_64 romm-frontend.x86_64 rombox.pck romm-frontend.pck

echo.
echo Copying macOS Release...
copy /y "build\macos\rombox-macos.zip" "releases\rombox-macos.zip" >nul

echo.
echo Release packages created successfully in the 'releases' folder!
echo You can now upload 'rombox-windows.zip', 'rombox-linux.zip', 'rombox-macos.zip',
echo 'romm-frontend-windows.zip' and 'romm-frontend-linux.zip' to GitHub Releases.
pause
