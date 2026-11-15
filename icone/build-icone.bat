@echo off
rem Gera ..\folego.ico (versao %1, padrao 1) e icone\previa.png a partir de Icone.cs.
cd /d "%~dp0"
set V=%1
if "%V%"=="" set V=1
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /out:icone.exe /r:System.Drawing.dll Icone.cs && .\icone.exe %V% ..\folego.ico previa.png && del icone.exe
