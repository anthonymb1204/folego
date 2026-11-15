#!/bin/bash
# Publica uma versão: ./publicar.sh 0.4.0 caminho/notas.md
# A 1ª linha das notas vira o título do commit; o resto vira o corpo do commit e as notas da Release.
# Troca a versão no código, compila, faz commit, envia e cria a Release com o Folego.exe.
set -e
cd "$(dirname "$0")"
V="$1"; N="$2"
[ -n "$V" ] && [ -f "$N" ] || { echo "uso: ./publicar.sh 0.4.0 notas.md"; exit 1; }

sed -i -E "s/AssemblyVersion\(\"[0-9.]+\"\)/AssemblyVersion(\"$V.0\")/; s/AssemblyFileVersion\(\"[0-9.]+\"\)/AssemblyFileVersion(\"$V.0\")/; s/AssemblyInformationalVersion\(\"[0-9.]+\"\)/AssemblyInformationalVersion(\"$V\")/" Folego.cs
sed -i -E "s/const string Versao = \"[0-9.]+\";/const string Versao = \"$V\";/" Ui.cs

taskkill //IM Folego.exe //F >/dev/null 2>&1 || true
sleep 1
powershell.exe -NoProfile -Command "Set-Location -LiteralPath '$(cygpath -w "$PWD")'; cmd /c .\\build.bat; if (-not (Test-Path .\\Folego.exe)) { exit 1 }"
VER=$(powershell.exe -NoProfile -Command "(Get-Item -LiteralPath '$(cygpath -w "$PWD")\\Folego.exe').VersionInfo.ProductVersion" | tr -d '\r')
[ "$VER" = "$V" ] || { echo "versão compilada ($VER) diferente de $V"; exit 1; }

TITULO=$(head -1 "$N")
CORPO=$(tail -n +2 "$N")
git add -A
git commit -q -F - <<EOF
Fôlego $V: $TITULO
$CORPO

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
git push -q
SHA=$(git rev-parse HEAD)

TMP="${TMPDIR:-/tmp}/folego-$V"; mkdir -p "$TMP"; cp Folego.exe "$TMP/Folego.exe"
printf '%s\n\nBaixe o **Folego.exe** e dê dois cliques: não precisa instalar nada.\n' "$CORPO" > "$TMP/notas.md"
gh release create "v$V" "$TMP/Folego.exe" -R anthonymb1204/folego --target "$SHA" --title "Fôlego $V" --latest --notes-file "$TMP/notas.md"
