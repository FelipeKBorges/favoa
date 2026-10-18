# Fávoa

[![CI](https://github.com/FelipeKBorges/favoa/actions/workflows/ci.yml/badge.svg)](https://github.com/FelipeKBorges/favoa/actions/workflows/ci.yml)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)
![Blazor Server](https://img.shields.io/badge/Blazor-Server-512BD4)
![SQLite](https://img.shields.io/badge/SQLite-EF%20Core-003B57)
[![Licença: MIT](https://img.shields.io/badge/Licen%C3%A7a-MIT-yellow.svg)](LICENSE)

🇺🇸 [Read in English](README.en.md)

**Painel pessoal de gestão da vida, local: finanças, tarefas, hábitos, treino e dieta num lugar só, rodando no próprio PC.** Cada área da vida é uma célula de uma colmeia que enche conforme o dia é cumprido. Os registros são feitos com frases simples (`uber 23`, `agachamento 5x5 100kg`, `bebi 500 ml`), e um servidor MCP escrito à mão expõe os dados para consulta e atualização.

Feito para uso da minha casa: um processo, um arquivo SQLite, nenhuma dependência de nuvem, com acesso pelo PC e pelo celular.

<!-- Screenshots: coloque as imagens em docs/img/ e descomente
![Colmeia](docs/img/colmeia.png)
![Captura por frase](docs/img/captura.gif)
-->

## Destaques técnicos

| Área | O que tem |
|---|---|
| **Arquitetura** | Solução .NET 10 em camadas: `Core` (domínio e regras puras), `Data` (serviços com EF Core), `Web` (Blazor Server) e `Mcp` (servidor stdio). Serviços separados por área, atrás de interfaces enxutas |
| **Multi-tenant** | Isolamento por família e por pessoa com filtros globais do EF Core. Dono preenchido automaticamente no `SaveChanges`. Falha fechada: sem usuário logado, o contexto usa um id que não existe. Todo dado pessoal novo ganha teste de separação |
| **Segurança** | Login por e-mail e senha com PBKDF2-SHA256 (210 mil iterações), bloqueio progressivo por conta e por origem, cookie HttpOnly + SameSite=Strict revalidado a cada pedido (trocar a senha desconecta todos os aparelhos), proteção contra redirecionamento aberto, convites guardados só como hash |
| **Servidor MCP** | Servidor Model Context Protocol escrito do zero (JSON-RPC 2.0 por stdin/stdout, sem SDK) com 13 ferramentas. Respostas em texto curto para economizar contexto; toda ferramenta que grava pede aprovação do usuário |
| **Captura por linguagem natural** | Interpretador de frases por regras (offline e determinístico) para despesas, receitas, água, tarefas, hábitos, treinos de força e cardio, refeições e peso, com prévia ao vivo e desfazer |
| **Web Push** | Notificações no celular sem pacote de terceiros: criptografia da mensagem (RFC 8291, aes128gcm) e assinatura VAPID (RFC 8292) |
| **Dados seguros** | Migrações EF Core versionadas (com adoção de bancos anteriores às migrações), backup diário consistente pela API de backup do SQLite, exportação/importação JSON versionada e validada antes de substituir numa transação, CSV pronto para o Excel em português, cópia opcional no OneDrive |
| **Cuidado com o domínio** | Dinheiro em centavos (`long`), `DateOnly` para datas de negócio, carimbos em UTC, comparação de nomes sem acento e sem maiúsculas, tabela de alimentos TACO embutida para uso offline |
| **Testes** | Mais de 300 casos de teste xUnit sobre os serviços reais com SQLite em memória, sem mocks |
| **Decisões** | Cada escolha de arquitetura registrada com o motivo em [docs/DECISOES.md](docs/DECISOES.md) |

## Arquitetura

```
Navegador (PC ou celular via Tailscale, instalável como PWA)
   │  SignalR (Blazor Server)
   ▼
Favoa.Web ── componentes Razor, login, autenticação por cookie
   │
   ▼
Favoa.Data ── serviços EF Core, migrações, backups, Web Push ──► SQLite (um arquivo local)
   ▲
   │
Favoa.Mcp ── servidor MCP (stdio)

Favoa.Core ── entidades, interpretador de frases, cálculo da colmeia (sem dependências)
```

Mais detalhes em [docs/ARQUITETURA.md](docs/ARQUITETURA.md).

## Tecnologias

C# · .NET 10 · ASP.NET Core · Blazor Server · Entity Framework Core · SQLite · xUnit · Model Context Protocol · Web Push / PWA · GitHub Actions · PowerShell (scripts de instalação e atualização)

## Rodar

Pré-requisito: [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
git clone https://github.com/FelipeKBorges/favoa.git
cd favoa
dotnet test
dotnet run --project src\Favoa.Web
```

Abra http://localhost:5080, crie sua casa no primeiro acesso, aperte **Ctrl + K** e escreva o que aconteceu.

## Documentação

- [docs/MANUAL.md](docs/MANUAL.md): manual de uso completo (frases da captura, telas, celular, MCP, backups)
- [docs/ARQUITETURA.md](docs/ARQUITETURA.md): arquitetura e modelo de dados
- [docs/DECISOES.md](docs/DECISOES.md): registro de decisões
- [docs/MVP.md](docs/MVP.md): escopo e próximos passos

## Licença

[MIT](LICENSE). Dados e fontes de terceiros mantêm suas licenças: tabela TACO via taco-api (MIT) e fontes Bricolage Grotesque e Manrope (SIL OFL).
