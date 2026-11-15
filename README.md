# Otimizador

App de um botão para o PC com Windows: libera RAM e limpa lixo sem fechar o jogo aberto. Baixe o `Otimizador.exe` em [Releases](../../releases/latest) e dê dois cliques, sem instalar nada.

**Otimizar**
- **Otimizar agora:** encolhe a RAM dos programas em segundo plano sem fechar nenhum (o Roblox, os jogos do Modo Jogo, o app da frente e o Windows ficam protegidos). Também limpa temporários com mais de 1 dia, cache de DNS e, se marcado, a lixeira.
- **Modo forte (admin):** também limpa o cache de memória (lista de espera, como o RAMMap) e o Temp/Windows Update do Windows.
- **Automático:** quando a RAM passa de 85%, otimiza sozinho (no máximo a cada 3 min). Minimizado, fica no canto da barra de tarefas.
- **Programas que mais gastam RAM:** lista para você escolher o que fechar (pede confirmação; serviços do Windows não aparecem).

**Inicialização** (0.2)
- Lista os programas que abrem junto com o Windows, com liga/desliga. Funciona igual ao Gerenciador de Tarefas: nada é apagado, e dá para religar quando quiser.

**Modo Jogo** (0.2)
- Quando um jogo da lista abre (vem com o Roblox), coloca o jogo em prioridade alta, libera RAM e fecha os programas que você escolher. Quando o jogo fecha, reabre esses programas.

Tudo que o app faz fica em `otimizador_log.txt`, ao lado do .exe. As preferências ficam em `%APPDATA%\Otimizador\config.txt`.

Compilar: `build.bat` (usa o C# que já vem no Windows).
