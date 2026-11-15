# Otimizador

App de um botão para o PC: libera RAM e limpa lixo sem fechar o jogo aberto (Roblox e o app em primeiro plano ficam protegidos).

- **Otimizar agora:** encolhe a RAM dos programas em segundo plano (não fecha nada), limpa temporários com mais de 1 dia, cache de DNS e, se marcado, a lixeira.
- **Modo forte (admin):** também limpa o cache de memória (lista de espera, como o RAMMap) e o Temp/Windows Update do Windows.
- **Automático:** quando a RAM passa de 85%, otimiza sozinho (no máximo a cada 3 min). Minimizado, fica no canto da barra de tarefas.
- **Programas que mais gastam RAM:** lista para você escolher o que fechar (pede confirmação; serviços do Windows não aparecem).
- Tudo que faz fica em `otimizador_log.txt`, ao lado do .exe.

Compilar: `build.bat` (usa o C# que já vem no Windows, sem instalar nada).
