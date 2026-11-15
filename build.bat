@echo off
rem Compila o Folego.exe com o C# que ja vem no Windows (.NET Framework 4).
cd /d "%~dp0"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ /utf8output /codepage:65001 /win32manifest:app.manifest /win32icon:folego.ico /out:Folego.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /resource:celular.html,celular.html Folego.cs Ui.cs Escala.cs Remoto.cs
