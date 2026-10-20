# Dar acesso a outra família

Cada casa tem dados separados (filtro por família) e entra com o próprio e-mail e senha. Tudo roda no seu PC: quem
administra o computador consegue abrir o banco, então avise a outra família disso.

## 1. Deixar o acesso só por HTTPS (uma vez, PowerShell como administrador, na pasta do projeto)

```powershell
powershell -ExecutionPolicy Bypass -File .\ferramentas\instalar-inicializacao.ps1 -ComTailscale -SoPeloTailscale -CadastroAberto
```

- `-ComTailscale` publica o Fávoa com HTTPS (`tailscale serve`); o cookie de acesso passa a sair com `Secure`.
- `-SoPeloTailscale` faz o app escutar só em `localhost`: ninguém alcança a porta 5080 por HTTP direto.
- `-CadastroAberto` mostra "Criar uma conta para a minha casa" na tela de entrada.

Depois confira: abra a URL `https://<seu-pc>.<tailnet>.ts.net/` no celular (a URL aparece no fim do script).

## 2. Liberar o PC para a outra família no Tailscale

Não coloque a família inteira na sua rede. No painel do Tailscale (login.tailscale.com > Machines), no seu PC: **⋯ > Share…**
e mande o link para a pessoa. Ela instala o Tailscale, aceita o compartilhamento e abre a URL do passo 1.
Só esse computador fica visível para ela.

## 3. A outra família cria a conta

Na tela de entrada: **Criar uma conta para a minha casa**. Ela informa o nome da casa (opcional), o nome, o e-mail e uma
senha longa. Isso cria uma casa nova, vazia, com ela como dona. Para chamar mais gente da mesma casa, ela usa
**Seus dados > Pessoas e acesso > Adicionar pessoa** (convite de 7 dias).

## 4. Fechar o cadastro

Assim que a casa nova existir, rode o mesmo comando **sem** `-CadastroAberto`:

```powershell
powershell -ExecutionPolicy Bypass -File .\ferramentas\instalar-inicializacao.ps1 -ComTailscale -SoPeloTailscale
```

## O que muda com duas casas

- Exportar, importar e "recomeçar do zero" só funcionam com **uma** família no banco; com duas ficam bloqueados.
- O backup diário copia o banco inteiro (as duas casas), na sua pasta de dados.
- Esqueceu a senha: só quem tem acesso ao PC redefine (`--redefinir-senha e-mail nova-senha`).
- Cookie vale 30 dias por aparelho; "Sair de todos os aparelhos" (em Seus dados) derruba todos.
