@echo off
rem Rebuild KeyDebounce.exe with the .NET Framework compiler that ships with Windows.
rem Pause is intentional: run this by double-clicking to see any compiler error.
setlocal
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo [x] csc.exe not found - is .NET Framework 4.x installed?
  pause & exit /b 1
)
rem three sources: KeyDebounce.cs (tray launcher + self-heal), KeyDebounce.Ui.cs
rem (主界面 / 按键管理 / 抖动扫描 / 实时日志镜像) and Lang.cs (中英双语)
taskkill /IM KeyDebounce.exe /F >nul 2>&1
timeout /t 1 /nobreak >nul
"%CSC%" /nologo /target:winexe /optimize+ /out:"%~dp0..\KeyDebounce.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll "%~dp0KeyDebounce.cs" "%~dp0KeyDebounce.Ui.cs" "%~dp0Lang.cs"
if errorlevel 1 ( echo [x] build failed & pause & exit /b 1 )
echo [+] built %~dp0..\KeyDebounce.exe
echo     start it again from the tray or by double-clicking it.
pause
