# Fávoa

Painel pessoal de gestão da vida. Roda no seu computador, guarda tudo localmente e se organiza como uma colmeia: cada área da vida é uma célula que enche conforme o dia é cumprido.

## Rodar pela primeira vez

Pré-requisito: [.NET 10 SDK](https://dotnet.microsoft.com/download) (confira com `dotnet --list-sdks`).

```powershell
cd Documents\favoa
dotnet build
dotnet test
dotnet run --project src\Favoa.Web
```

Abra http://localhost:5080. Aperte **Ctrl + K** e escreva o que aconteceu.

## O que dá para dizer na captura

| Frase | O que acontece |
| --- | --- |
| `uber 23` / `mercado 150` / `ifood 62,90` | Despesa: basta o que foi e o valor. A categoria vem da palavra (uber → Transporte) |
| `salário 5000` / `freela 800` | Receita (palavras como salário, freela, reembolso, rendimento) |
| `gastei 32 no mercado com o nubank` | Despesa de R$ 32,00, categoria Mercado, conta Nubank |
| `recebi 5000 de salário` | Receita |
| `orçamento 3000` | Define o orçamento mensal (enche a célula de Finanças) |
| `nova conta Itaú` | Cria uma conta |
| `bebi 500 ml` / `bebi 1,5 l` | Água do dia |
| `tarefa pagar luz sexta` | Tarefa com prazo (hoje, amanhã, ontem, dia da semana ou dd/mm) |
| `novo hábito ler 20 min` | Cria um hábito |
| `fiz ler 20 min` ou só `ler 20 min` | Marca o hábito de hoje |
| `supino 3x10 40kg` / `agachamento 5x5 100` | Exercício de força no treino do dia |
| `corri 5 km em 30 min` / `pedalei 1h20` | Cardio |
| `comi 150g de arroz no almoço` | Refeição (alimento da TACO ou seu; sem refeição, usa a hora) |
| `tomei 1 scoop de whey` | Usa a porção cadastrada no alimento |
| `peso 82,5` | Peso do dia (de novo no mesmo dia, substitui) |

Acrescente `ontem` numa despesa para lançar no dia anterior. O que sobrar da frase vira a descrição: `gastei 45 na farmácia remédio de gripe` → categoria Saúde, descrição "Farmácia remédio de gripe".
Na captura, **Enter** ou o botão **Salvar** grava; se a frase não der para salvar, a prévia diz o porquê.

## Projetos

Em **Projetos** (no PC, no menu do topo; no celular, em Mais), crie uma viagem, um trabalho ou um plano da casa.
Cada projeto junta tarefas, gastos, checklist, datas importantes e notas. Viagem, trabalho e casa já começam com uma lista típica.
Para ligar algo ao projeto pela frase, use `#` com uma palavra do nome: *hotel 1200 #chile*, *tarefa comprar adaptador #chile*.
As datas do projeto aparecem no calendário, e a célula Projetos da colmeia mostra o andamento.

## Início: colmeia ou calendário

No topo da tela inicial, escolha **Colmeia** ou **Calendário** (fica salvo para cada pessoa).
O calendário mostra o mês (ou a semana) com tarefas, contas previstas, treinos, peso e gastos de cada dia, e um anel com quanto a colmeia encheu nos dias passados.
Clique num dia para ver o detalhe: concluir tarefas, marcar hábitos esquecidos e criar uma tarefa já com aquela data.
Embaixo, **Progresso** traz a linha dos últimos 30 ou 90 dias (geral ou por área) e o mapa de constância dos últimos seis meses.

## Finanças

Clique em **Finanças** no topo (ou na célula de Finanças da colmeia):

- **Lançar** abre um formulário simples: despesa ou receita, data, descrição e valor. A categoria sai da descrição ("uber" → Transporte; sem palavra conhecida, Outros) e a conta é a padrão; dá para ajustar depois clicando no lançamento. Marque **Repetir todo mês** para virar recorrente.
- Em "Para onde foi", categorias com limite mostram quanto já foi do limite e quanto resta. Ao lançar, o aviso diz quando passa de 80% ou estoura. Atalho: `/financas?novo=true` (bom para salvar na tela de início do celular).
- Receitas, despesas e saldo do mês, e o orçamento com uma marca do quanto já era esperado ter gasto até hoje, mais quanto ainda dá para gastar por dia.
- Lançamentos agrupados por dia; clique em um para editar ou excluir.
- Filtros por tipo, conta e busca. Clique numa categoria em "Para onde foi" para ver só ela.
- Setas para trocar de mês. Mês e filtros ficam na URL, então dá para favoritar.

## Tarefas

Em **Tarefas** (topo ou célula da colmeia):

- Atrasadas, hoje, próximas (por dia), sem prazo e concluídas dos últimos 14 dias.
- Escreva no campo do topo como na captura: `pagar luz sexta`, `dentista 20/10`.
- Clique no prazo de uma tarefa para trocar com um clique (hoje, amanhã, próxima semana, sem prazo ou outra data).
- Clique no título para editar ou excluir.

## Hábitos

Em **Hábitos** (topo ou célula da colmeia):

- Uma linha de hexágonos com os últimos 7 dias: clique para marcar ou desmarcar, inclusive um dia que você esqueceu.
- Sequência atual (conta até ontem se hoje ainda não foi feito), melhor sequência e trilha dos últimos 28 dias.
- Nos três pontinhos: arquivar (sai do painel, mas guarda o histórico) ou excluir.

## Treino

Em **Treino** (topo ou célula Treino da colmeia):

- **Fichas**: monte Treino A, B, C com exercícios, séries, repetições e carga (cardio: minutos). Nomes como "esteira" ou "corrida" viram cardio sozinhos.
- **Hoje**: o Fávoa sugere a próxima ficha da rotação. Ao começar, cada exercício já vem com o que você fez da última vez; ajuste a carga, marque o que fez e salve. Dá para acrescentar exercícios na hora.
- **Histórico** com volume (séries × reps × carga) e minutos de cardio; **Exercícios** com gráfico de evolução da carga e recorde.
- A célula Treino da colmeia enche conforme a meta de treinos por semana (em Dieta → Metas).

## Dieta e peso

Em **Dieta** (topo ou célula Alimentação):

- **Hoje**: calorias, proteína, carboidrato, gordura e água contra as metas; cada refeição com o que foi comido, "Segui o plano" num clique e busca de alimentos para adicionar.
- **Plano**: o que você pretende comer em cada refeição, com o total de calorias e macros do dia.
- **Alimentos**: a base vem com cerca de 590 alimentos da Tabela TACO (UNICAMP), sem internet. Cadastre os seus (valores por 100 g e porção caseira, como "1 scoop = 30 g").
- **Metas**: calorias, macros e treinos por semana.
- **Peso**: registro diário com gráfico dos últimos 90 dias e variação em ~30 dias.

## Cadastros (ícone de ajustes no topo)

- **Categorias:** criar, renomear e definir um **limite por mês** em cada categoria de despesa. Excluir uma categoria com lançamentos pede para onde movê-los (serve para juntar duas categorias).
- **Contas:** criar, renomear, arquivar (sai das opções, o histórico fica) e excluir movendo os lançamentos para outra conta. Mostra o saldo do que foi lançado em cada uma.
- **Recorrentes:** aluguel, internet, assinaturas, salário. O Fávoa lança sozinho todo mês no dia certo (em meses sem dia 31, no último dia). Pausar não lança os meses parados; apagar um lançamento gerado não faz ele voltar. Se o PC ficou desligado no dia, lança quando ligar.
- **Lembretes:** texto livre ("Beber água" às 10h nos dias úteis) ou **Tarefas de hoje** (lista as tarefas do dia e as atrasadas, ou avisa que não há nenhuma). Chegam como notificação no celular e no navegador do PC. Cada lembrete mostra o próximo aviso e o resultado do último envio; **Testar** manda ele na hora.

### Notificações no celular

1. Abra o Fávoa pelo endereço do Tailscale (`https://….ts.net`). No iPhone, antes, instale: Safari → Compartilhar → **Adicionar à Tela de Início**, e abra pelo ícone.
2. Em **Cadastros → Lembretes**, toque em **Ativar neste aparelho** e permita as notificações.
3. Toque em **Enviar teste**.

O PC precisa estar ligado na hora do lembrete (se ligar até 2 horas depois, o aviso ainda sai). As chaves das notificações ficam só neste PC e não vão na exportação.

## Errou? Desfaça ou edite

- Logo depois de salvar, o aviso no rodapé mostra **Desfazer** por alguns segundos. Se a frase criou uma conta ou categoria por engano (um erro de digitação, por exemplo), ela some junto.
- Clique em qualquer item de **Últimos registros** para editar valor, data, categoria, conta ou descrição, ou para excluir.
- Nas tarefas, o lápis ao lado abre a edição (título, prazo, concluída).
- No modal, `Enter` salva e `Esc` fecha. Excluir pede confirmação.

## Abrir sozinho com o Windows

```powershell
powershell -ExecutionPolicy Bypass -File .\ferramentas\instalar-inicializacao.ps1
```

Rode num PowerShell **como administrador**. Publica o Fávoa em `%LOCALAPPDATA%\Favoa\app`, cria a tarefa "Favoa" que o roda em segundo plano de verdade (sem janela, sobe quando o PC liga, com o seu usuário e sem pedir senha), põe o atalho **Fávoa** no menu Iniciar e o ícone **Atualizar Fávoa** na área de trabalho. Log em `%LOCALAPPDATA%\Favoa\logs\favoa.log`. Sem administrador, ele roda na sua sessão e pode cair se a janela de console for fechada.

- Para desfazer: `... -File .\ferramentas\instalar-inicializacao.ps1 -Remover` (os dados ficam).
- Para desenvolver com o Fávoa rodando em segundo plano, use outra porta: `dotnet run --project src\Favoa.Web --urls http://localhost:5081`.

## Atualizar com um clique

Dê dois cliques em **Atualizar Fávoa** (área de trabalho ou menu Iniciar) e confirme a permissão do Windows. Ele:

1. Compila a versão que está na pasta do projeto, com o Fávoa ainda no ar. Se falhar, nada muda.
2. Para o Fávoa e guarda um backup do banco em `Backups\antes-atualizar-…` (mantém os 5 últimos).
3. Troca a versão e religa. Se a nova não subir, volta sozinho para a anterior.

A versão que está rodando aparece no rodapé de **Seus dados**. Sem o ícone ainda? Rode uma vez: `powershell -ExecutionPolicy Bypass -File .\ferramentas\atualizar.ps1`.

## Casal: o que é da casa e o que é de cada um

O Fávoa pode ter mais de uma pessoa (ex.: Davi e Eva):

- **Da casa:** finanças (lançamentos, contas, categorias, limites, recorrentes) e tarefas. Cada lançamento guarda **quem lançou**; em Finanças dá para filtrar **Casa | Davi | Eva**, e uma inicial colorida mostra quem lançou cada um.
- **De cada um:** treino, dieta (plano, refeições, metas), peso, hábitos, água e lembretes. Cada lembrete toca só nos aparelhos do dono. A base de alimentos e a lista de exercícios são compartilhadas.

Primeiro acesso: no PC, abra `/primeiro-acesso` e crie o seu login (apelido, e-mail e senha). Quem cria é o dono da casa. Para adicionar alguém: **Seus dados → Pessoas e acesso → + Adicionar pessoa** e depois **Convidar**; a pessoa recebe um link de uso único (vale 7 dias) e escolhe o próprio apelido, e-mail e senha. Em bancos criados antes dos logins, digite no primeiro acesso o apelido que já aparece nos seus registros para continuar com eles.

## Usar no celular

**Opção 1 — Tailscale (recomendado):** HTTPS de verdade, funciona fora de casa e instala como app.

1. Instale o [Tailscale](https://tailscale.com/download) no PC e no celular e entre com a mesma conta (grátis para uso pessoal).
2. Rode `... -File .\ferramentas\instalar-inicializacao.ps1 -ComTailscale`. Na primeira vez, o Tailscale pode pedir para ativar HTTPS no seu tailnet: siga o link e rode de novo.
3. Abra no celular a URL que o script mostrar (`https://seu-pc.....ts.net`), entre com o seu e-mail e senha e instale:
   - **Android (Chrome):** menu ⋮ → **Instalar app**.
   - **iPhone (Safari):** Compartilhar → **Adicionar à Tela de Início**.

**Opção 2 — Wi-Fi de casa:** rode `... -File .\ferramentas\abrir-no-celular.ps1` uma vez **como administrador** (libera a porta 5080 só para redes privadas) e abra `http://<ip-do-pc>:5080` no celular. Sem HTTPS, o Android cria só um atalho (abre no navegador); o iPhone costuma abrir em tela cheia.

Segurança:
- Todo aparelho pede e-mail e senha, inclusive o próprio PC. O primeiro acesso só abre no PC onde o Fávoa roda.
- Senha com no mínimo 8 caracteres, sem senhas comuns, guardada com hash PBKDF2; aparelho lembrado por 30 dias; trocar a senha ou "Sair de todos os aparelhos" derruba as outras sessões.
- 5 erros seguidos bloqueiam a conta por 1 minuto, dobrando a cada rodada (até 15 minutos), e há limite por origem.
- Esqueceu a senha: no PC, `dotnet Favoa.Web.dll --redefinir-senha seu@email.com NovaSenhaLonga`.
- Os dados de acesso não saem na exportação e não são mexidos pela importação.
- Na opção 2 a conexão não é criptografada: use só na rede de casa. Não abra a porta no roteador.

## Integração MCP

O Fávoa expõe seus dados por um servidor MCP (Model Context Protocol). Um cliente MCP conectado atende pedidos como:
*"monte no Fávoa um plano de ganho de peso com aveia, banana, ovos e frango, 3.000 kcal"* ou *"como foram meus gastos este mês?"*.
As consultas são livres; tudo que grava pede a sua aprovação antes.

Roda localmente, sem chave de API: os dados só saem do PC quando o cliente consulta algo.

**Instalar** (na pasta do projeto):

```powershell
dotnet publish src\Favoa.Mcp -c Release -o $env:LOCALAPPDATA\Favoa\mcp
```

Depois, na configuração do seu cliente MCP, adicione um servidor `favoa` com o comando `dotnet` e o argumento `%LOCALAPPDATA%\Favoa\mcp\Favoa.Mcp.dll`. Publique de novo quando atualizar o Fávoa e reinicie o cliente.

Para falar de outra pessoa, diga o nome: *"monte no Fávoa a dieta da Eva"* (todas as ferramentas aceitam `pessoa`; sem ela, vale a primeira pessoa da casa).

Ferramentas disponíveis: resumo do dia, finanças do mês, tarefas, treinos e evolução de carga, dieta do dia, buscar alimentos (TACO), montar o plano de uma refeição, criar fichas de treino, definir metas, cadastrar alimentos e registrar qualquer frase da captura.
Se o Fávoa estiver aberto no navegador, recarregue a página para ver o que foi gravado pelo MCP.

## Seus dados (ícone de banco no canto do topo)

- Banco: `%LOCALAPPDATA%\Favoa\favoa.db`
- Backups diários: `%LOCALAPPDATA%\Favoa\Backups` (mantém os últimos 14). Também dá para fazer um backup na hora.
- **Exportar:** tudo em JSON (para guardar ou levar para outro computador) ou CSV por módulo (lançamentos, tarefas, hábitos, água, treinos, refeições e peso), que abre direto no Excel.
- **Importar:** escolha um JSON do Fávoa, confira a comparação "hoje × arquivo" e confirme. Substitui tudo, mas antes salva um backup `antes-importar-…`.
- **Restaurar um backup:** feche o Fávoa, copie o arquivo para `%LOCALAPPDATA%\Favoa\favoa.db` e abra de novo.

### Cópia no OneDrive

Em **Seus dados → Cópia no OneDrive**, o Fávoa já sugere a pasta do seu OneDrive pessoal (`…\OneDrive\Fávoa`) e a pasta do projeto. Clique em **Ligar e copiar agora**. A partir daí ele mantém lá, sozinho:

- `Backups\favoa-AAAA-MM-DD.db`: uma cópia do banco por dia (as últimas 30).
- `Dados\favoa.db`, `Dados\favoa-dados.json` e `Dados\csv\*.csv`: o banco, todas as tabelas em JSON (importável em Seus dados) e em planilhas. Atualizados a cada 30 minutos quando algo muda.
- `Codigo\favoa.bundle`: o projeto com todo o histórico, atualizado quando há commit novo. Para recriar em outro PC: `git clone favoa.bundle favoa`.

Para restaurar num PC novo: instale o .NET, recrie o projeto pelo `favoa.bundle`, rode `instalar-inicializacao.ps1` e importe o `favoa-dados.json` em Seus dados (ou copie `Dados\favoa.db` para `%LOCALAPPDATA%\Favoa`). A configuração da cópia fica em `%LOCALAPPDATA%\Favoa\nuvem.json`.

## Documentação

- [docs/ARQUITETURA.md](docs/ARQUITETURA.md)
- [docs/DECISOES.md](docs/DECISOES.md)
- [docs/MVP.md](docs/MVP.md)
