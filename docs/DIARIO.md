# Diário

Cada pessoa tem o próprio diário (uma entrada por dia): humor de 1 a 5, energia de 1 a 5, texto livre e tags. Em **Diário** (menu "Mais" no celular, barra de cima no computador) há duas abas: **Dia** (escrever) e **Linha do tempo** (calendário do mês colorido pelo humor, busca no texto e filtro por tag).

## Privacidade

- O diário é **pessoal**, não da casa: nem a outra pessoa da mesma casa, nem o dono da conta veem o seu. O filtro é por casa **e** por pessoa, e há testes que garantem isso.
- O **texto e as tags ficam cifrados** no banco (AES-256-GCM). Humor e energia ficam abertos porque alimentam o calendário.
- Backup, cópia do OneDrive e o JSON de "Seus dados" levam o diário **cifrado**. O CSV não inclui o diário.
- Quem administra o computador onde o Fávoa roda guarda a chave e, tecnicamente, consegue ler. A cifra protege contra quem copia só o arquivo do banco.

## A chave

- Fica em `chaves\diario.key`, na pasta de dados do Fávoa, e é criada no primeiro uso.
- **Ela não vai na cópia do OneDrive de propósito.** Guarde uma cópia fora do computador: em Diário, "baixar a chave" (só quem criou a conta da casa, no próprio computador).
- Sem a chave, as entradas aparecem como "não foi possível abrir". Para restaurar num computador novo: crie a pasta `chaves` dentro da pasta de dados, salve a linha da chave sozinha em `chaves\diario.key` e só então abra o Fávoa. Não deixe o app criar outra chave por cima.

## O que ainda não existe

Resumo automático do dia (treino, refeições, gastos), revisão semanal, lembrete diário, memórias de anos anteriores, anexos e lixeira. O MCP não lê o diário de propósito.
