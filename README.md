# Fôlego

"Dá um fôlego pro PC." Libera RAM, limpa lixo e prepara o Windows pro jogo, sem fechar nada que importa. (Até a 0.2.0 o app se chamava Otimizador.)

Baixe o `Folego.exe` em [Releases](../../releases/latest) e dê dois cliques: não precisa instalar nada.

## Telas

**Início**
- **Memória:** anel com quanto da RAM está em uso (fica vermelho acima de 85%).
- **Dar fôlego:** o botão principal. Encolhe a RAM dos programas em segundo plano sem fechar nenhum (o Roblox, os jogos do Modo Jogo, o app da frente e o Windows ficam protegidos) e, conforme os botõezinhos embaixo, apaga temporários com mais de 1 dia, limpa o cache de DNS e esvazia a lixeira.
- **Sistema:** processador, disco C: e quanto o Fôlego já liberou hoje.
- **Gastando mais RAM:** os programas que mais pesam, com ícone; passe o mouse e clique no × para fechar (pede confirmação).
- **Modo Jogo e atividade de hoje.**

**Inicialização:** os programas que abrem com o Windows, com chave liga/desliga. Funciona igual ao Gerenciador de Tarefas: nada é apagado.

**Modo Jogo:** quando um jogo da lista abre (vem com o Roblox), o jogo vai para prioridade alta, a RAM é liberada e os programas escolhidos fecham; quando o jogo fecha, eles voltam.

**Ajustes:** o que o Dar fôlego limpa, o automático, o **Fôlego programado** (a cada 15 min, 30 min, 1 hora ou 2 horas), o papel de parede no fundo (o do Windows, desfocado, ou roxo com montanhas), modo forte (admin) e o registro de atividades.

O **modo forte** (escudo no menu) limpa também o cache de memória e o Temp do Windows.

Tudo que o app faz fica em `folego_log.txt`, ao lado do .exe. As preferências ficam em `%APPDATA%\Folego\config.txt`.

## Código

- `Folego.cs`: o motor (memória, limpeza, inicialização, processos).
- `Ui.cs`: a tela, toda desenhada à mão.
- `icone/`: o ícone, desenhado em código (`build-icone.bat`).
- Compilar: `build.bat` (usa o C# que já vem no Windows). Publicar uma versão: `./publicar.sh 0.4.0 notas.md`.
