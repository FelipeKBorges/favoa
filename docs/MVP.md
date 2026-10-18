# Escopo do MVP

## Entregue na primeira sessão

- [x] Solução .NET 10 em camadas (Core, Data, Web, Tests)
- [x] Colmeia do dia com 7 células e cálculo testado
- [x] Captura universal (Ctrl+K) com prévia ao vivo
- [x] Interpretador de frases: despesa, receita, orçamento, conta, água, tarefa, hábito
- [x] Painel "Agora": próximas tarefas, hábitos pendentes, últimos registros
- [x] Banco SQLite local com dados iniciais (categorias e conta Carteira)
- [x] Backup diário automático
- [x] Layout responsivo (computador e celular)

## Próximas entregas do MVP

- [x] Editar e excluir lançamento, água, tarefa e marcação de hábito (modal de edição)
- [x] Tela de Finanças: mês por dia, totais, ritmo do orçamento, filtros (tipo, categoria, conta, busca)
- [x] Tela de Tarefas: atrasadas, hoje, próximas por dia, sem prazo, concluídas; prazo com um clique; criar direto na tela
- [x] Tela de Hábitos: grade dos últimos 7 dias (marca dias passados), sequência atual e melhor, trilha de 28 dias, arquivar/reativar/excluir
- [x] Desfazer o último registro direto no aviso
- [ ] Editar campos da prévia com Tab antes de salvar
- [x] Exportar e importar JSON (com prévia e backup antes); exportar CSV por módulo; backup manual
- [x] Descrição do lançamento = o que sobra da frase (sem valor, conta, categoria e data)
- [x] Fontes hospedadas localmente
- [x] Primeira migração EF Core (com adoção de bancos antigos)

## Fase 2 — área Fitness (entregue)

- [x] Treino: exercícios (grupo muscular inferido), fichas A/B/C, treino do dia pela ficha com a última carga, rotação, cardio
- [x] Histórico de treinos, volume e evolução de carga por exercício (gráfico)
- [x] Dieta: base TACO offline, alimentos próprios com porção, plano por refeição, "segui o plano", registro do dia
- [x] Metas de calorias, macros e treinos por semana
- [x] Peso diário com gráfico
- [x] Captura: força, cardio, refeição e peso; desfazer e excluir
- [x] Colmeia: células Treino e Alimentação (água + calorias)
- [x] Exportar/importar e CSV incluindo Fitness

## Lançamento de finanças revisado (entregue)

- [x] Frase curta: "uber 23", "salário 5000"; categoria por palavra-chave
- [x] Botão Salvar na captura (no celular não dependia só do Enter)
- [x] Formulário "Novo lançamento" em Finanças: tipo, data, descrição e valor (categoria automática pela descrição)

## Organização e avisos (entregue)

- [x] Lançamentos recorrentes (lançados sozinhos no dia; pausar, editar, excluir; "Repetir todo mês" no formulário)
- [x] Lembretes com notificação no celular e no PC (texto livre ou tarefas do dia; dias da semana)
- [x] Orçamento por categoria (limite mensal, barra em "Para onde foi", aviso ao lançar)
- [x] Tela de Cadastros: categorias, contas (arquivar, juntar), recorrentes e lembretes
- [x] Atualizar com um clique (ícone "Atualizar Fávoa", troca segura com volta automática)
- [x] Cópia contínua no OneDrive: backups diários, dados (JSON/CSV) e código (git bundle)

## Casal (entregue)

- [x] Pessoas da casa com PIN próprio; o PIN abre o perfil certo no celular; seletor no topo no PC
- [x] Treino, dieta, peso, metas, hábitos, água e lembretes separados por pessoa (filtro global no banco)
- [x] Finanças e tarefas da casa, com quem lançou e filtro por pessoa
- [x] Notificações só nos aparelhos do dono; MCP com `pessoa`; exportar/importar v4 com pessoas

## Próximos

Importar extrato do banco (OFX/CSV), cartão de crédito por fatura, revisão da semana, ajustar a prévia da captura com Tab, Projetos, calendário, relatórios.

## Acesso pelo celular (entregue)

- [x] PIN para aparelhos que não são o próprio PC (hash PBKDF2, bloqueio progressivo, aparelho lembrado 30 dias)
- [x] Script `abrir-no-celular.ps1` (firewall só em rede privada, mostra a URL)
- [x] App instalável (PWA): manifesto, ícones, página "PC desligado"
- [x] Abrir com o Windows (`instalar-inicializacao.ps1`) e publicação opcional no Tailscale com HTTPS
- [x] Acesso via Tailscale serve também pede PIN
