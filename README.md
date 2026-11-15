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

**Modo Jogo:** quando um jogo da lista abre (vem com o Roblox), o jogo vai para prioridade alta, a RAM é liberada e os programas escolhidos fecham; quando o jogo fecha, eles voltam. Com o jogo aberto, o PC não dorme por inatividade (bom para farm).

**Mais FPS:** liga e desliga ajustes do Windows que ajudam o jogo (Modo de Jogo, sem gravação do Xbox em segundo plano, Edge sem rodar escondido, otimizações para jogos em janela), mostra a placa de vídeo e, nas placas AMD, o atalho do AFMF (geração de quadros) e o passo a passo para ligar AFMF, Anti-Lag e RSR no AMD Adrenalin. Nas placas AMD, o Fôlego confere a cada 12 horas (na página oficial da AMD) se saiu driver novo, mostra "em dia" ou "versão nova" e tem um botão para a página de download oficial da placa. Tem também a **escala nítida** (experimental): com o jogo numa janela menor, Ctrl+Alt+F estica a janela para a tela toda sem borrar (usa a ampliação do próprio Windows); sair do jogo desliga sozinha.

**Ajustes:** o que o Dar fôlego limpa, o automático, o **Fôlego programado** (a cada 15 min, 30 min, 1 hora ou 2 horas), o papel de parede no fundo (o do Windows, desfocado, ou roxo com montanhas), modo forte (admin) e o registro de atividades.

O **modo forte** (escudo no menu) limpa também o cache de memória e o Temp do Windows.

**No celular:** nos Ajustes, ligue "No celular" (pede o modo forte, para liberar o Fôlego no firewall só em redes privadas). No navegador do celular, no mesmo Wi-Fi, abra o endereço que aparece (ex.: `http://192.168.0.59:47474`) e digite o código de 6 dígitos. Dá para dar fôlego, ver a RAM, ligar e desligar as opções, o Modo Jogo, o programado, a inicialização e fechar programas. Use "Adicionar à tela inicial" para virar um ícone de app. Fora de casa, funciona pelo Tailscale. Só responde a aparelhos da rede local, cada código vale uma vez e 5 erros travam o pareamento por 5 minutos.

**Abrir com o Windows** (Ajustes): o Fôlego abre escondido no cantinho da barra de tarefas quando o PC liga, já no modo forte (tarefa agendada "Folego"), para o programado, o Modo Jogo e o celular funcionarem sempre.

**Sem atrapalhar o jogo:** com algo automático ligado (automático, programado ou Modo Jogo), fechar no X ou minimizar deixa o Fôlego no cantinho da barra de tarefas (botão direito no ícone: Abrir, Dar fôlego agora, Sair). Os fôlegos automáticos não mostram avisos nem abrem a janela, e erros vão para o registro. `Folego.exe --bandeja` abre direto no cantinho, sem janela. O fundo acompanha o papel de parede do Windows quando ele muda.

Tudo que o app faz fica em `folego_log.txt`, ao lado do .exe. As preferências ficam em `%APPDATA%\Folego\config.txt`.

## Código

- `Folego.cs`: o motor (memória, limpeza, inicialização, processos).
- `Ui.cs`: a tela, toda desenhada à mão.
- `Escala.cs`: a escala nítida (ampliação do Windows).
- `Remoto.cs` e `celular.html`: o servidor e a página do celular (vai dentro do .exe).
- `icone/`: o ícone, desenhado em código (`build-icone.bat`).
- Compilar: `build.bat` (usa o C# que já vem no Windows). Publicar uma versão: `./publicar.sh 0.4.0 notas.md`.
