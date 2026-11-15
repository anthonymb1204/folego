@echo off
rem Compila o Otimizador.exe com o C# que ja vem no Windows (.NET Framework 4).
cd /d "%~dp0"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ /utf8output /codepage:65001 /win32manifest:app.manifest /out:Otimizador.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll Otimizador.cs
