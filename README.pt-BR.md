# Fávoa

🇺🇸 [Read in English](README.md)

**Painel pessoal de gestão da vida, local: finanças, tarefas, hábitos, treino e dieta num lugar só, rodando no meu próprio PC.** Cada área da vida é uma célula de uma colmeia que enche conforme o dia é cumprido. Os registros são feitos com frases simples (`uber 23`, `agachamento 5x5 100kg`, `bebi 500 ml`), e um servidor MCP escrito à mão expõe os dados para consulta e atualização.

<!-- Screenshots: coloque as imagens em docs/img/ e descomente
![Colmeia](docs/img/colmeia.png)
![Captura por frase](docs/img/captura.gif)
-->

## Destaques técnicos

- **Arquitetura em camadas (.NET 10):** `Core` (domínio e regras puras), `Data` (serviços com EF Core), `Web` (Blazor Server) e `Mcp` (servidor stdio).
- **Multi-tenant:** isolamento por família e por pessoa com filtros globais do EF Core, dono preenchido no `SaveChanges` e falha fechada (sem login, nada aparece). Todo dado pessoal novo ganha teste de separação.
- **Segurança:** login com PBKDF2-SHA256 (210 mil iterações), bloqueio progressivo por conta e por origem, cookie HttpOnly + SameSite=Strict revalidado a cada pedido, proteção contra redirecionamento aberto, convites guardados só como hash.
- **Servidor MCP:** servidor MCP (JSON-RPC 2.0 por stdin/stdout, sem SDK) com 13 ferramentas; as que gravam pedem aprovação do usuário.
- **Captura por linguagem natural:** interpretador de frases por regras (offline e determinístico), com prévia ao vivo e desfazer.
- **Web Push sem pacotes:** criptografia RFC 8291 e assinatura VAPID RFC 8292.
- **Dados seguros:** migrações versionadas, backup diário consistente do SQLite, exportação/importação JSON versionada e validada, CSV pronto para o Excel, cópia opcional no OneDrive.
- **Mais de 300 casos de teste** xUnit sobre os serviços reais com SQLite em memória.
- **Registro de decisões** com o motivo de cada escolha em [docs/DECISOES.md](docs/DECISOES.md).

## Rodar

Pré-requisito: [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
git clone https://github.com/FelipeKBorges/favoa.git
cd favoa
dotnet test
dotnet run --project src\Favoa.Web
```

Abra http://localhost:5080, crie sua casa no primeiro acesso e aperte **Ctrl + K**.

## Documentação

- [docs/MANUAL.md](docs/MANUAL.md): manual de uso completo (frases da captura, telas, celular, MCP, backups)
- [docs/ARQUITETURA.md](docs/ARQUITETURA.md)
- [docs/DECISOES.md](docs/DECISOES.md)
- [docs/MVP.md](docs/MVP.md)

## Licença

[MIT](LICENSE). Dados e fontes de terceiros mantêm suas licenças: tabela TACO via taco-api (MIT) e fontes Bricolage Grotesque e Manrope (SIL OFL).
