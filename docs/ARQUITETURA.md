# Arquitetura

## Visão geral

```
Navegador (localhost:5080)
   │  SignalR (Blazor Server)
   ▼
Favoa.Web   ── componentes: MainLayout, Home, Colmeia, Captura
   │  IFavoaServico
   ▼
Favoa.Data  ── FavoaServico + FavoaDbContext (EF Core)  ── BackupDiario (BackgroundService)
   │
   ▼
SQLite: %LOCALAPPDATA%\Favoa\favoa.db        Backups: ...\Favoa\Backups\favoa-AAAA-MM-DD.db

Favoa.Core  ── usado por todos: entidades, InterpretadorFrase, CalculadoraColmeia
```

## Fluxo da captura universal

1. Ctrl+K (atalho em `wwwroot/favoa.js`) ou clique no campo do topo abre `Captura.razor`.
2. Ao abrir, carrega o `ContextoCaptura` (categorias, contas e hábitos existentes).
3. A cada tecla, `InterpretadorFrase.Interpretar` roda no servidor, em memória, e a prévia mostra módulo e campos.
4. Enter → `IFavoaServico.RegistrarAsync`. Contas e categorias novas são criadas na hora.
5. `EventosFavoa.RegistroSalvo` avisa a Home, que recarrega o painel.

## Colmeia

`CalculadoraColmeia.Calcular(ResumoDoDia)` devolve 7 células na ordem das posições do layout:

| Célula | Enche com |
| --- | --- |
| Finanças | parte do orçamento mensal ainda livre |
| Alimentação | água do dia ÷ meta (padrão 2,5 L) |
| Tarefas | concluídas hoje ÷ (concluídas + para hoje + atrasadas); sem tarefas = cheia |
| Hábitos | feitos hoje ÷ ativos |
| Hoje (centro) | média das quatro acima |
| Treino, Projetos | "em breve" (fase 2) |

## Modelo de dados

| Tabela | Campos principais |
| --- | --- |
| fin_conta | Id, Nome (único), Ativa |
| fin_categoria | Id, Nome, Tipo (despesa/receita) — único por (Nome, Tipo) |
| fin_lancamento | Id, ContaId, CategoriaId, Tipo, ValorCentavos, Data, Descricao |
| rot_tarefa | Id, Titulo, Prazo?, ConcluidaEm? |
| rot_habito | Id, Nome (único), Ativo |
| rot_habito_registro | Id, HabitoId, Data — único por (HabitoId, Data) |
| rot_agua | Id, Mililitros, Data |
| cfg_configuracao | Chave, Valor (orçamento, meta de água) |

## Crescimento

- Novo módulo (Treino, Alimentação completa, Projetos): entidades em `Core/<Modulo>`, tabelas com novo prefixo, métodos no serviço, célula na colmeia, regras no interpretador.
- Se `FavoaServico` crescer demais, dividir por módulo mantendo `IFavoaServico` como fachada.
- Acesso pelo celular: escutar na rede local, exigir PIN e HTTPS. Fora de casa: VPN pessoal.
