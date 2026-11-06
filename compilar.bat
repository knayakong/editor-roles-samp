@echo off
rem Compila el Editor de roles con el compilador C# que YA trae Windows (.NET Framework 4.x).
rem No descarga nada ni necesita Visual Studio / SDK.
setlocal
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
if not exist "%FW%\csc.exe" set FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319
set WM=%WINDIR%\System32\WinMetadata
cd /d "%~dp0"
"%FW%\csc.exe" -nologo -optimize+ -target:winexe -platform:anycpu -out:EditorRoles.exe -win32icon:icono\icono.ico ^
  -r:System.dll -r:System.Core.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll ^
  -r:"%FW%\System.Runtime.dll" -r:"%FW%\System.Runtime.WindowsRuntime.dll" ^
  -r:"%WM%\Windows.Foundation.winmd" -r:"%WM%\Windows.Media.winmd" -r:"%WM%\Windows.Graphics.winmd" ^
  -r:"%WM%\Windows.Storage.winmd" -r:"%WM%\Windows.Globalization.winmd" ^
  src\*.cs
if errorlevel 1 (echo. & echo *** Fallo la compilacion *** & pause & exit /b 1)
echo Listo: EditorRoles.exe
