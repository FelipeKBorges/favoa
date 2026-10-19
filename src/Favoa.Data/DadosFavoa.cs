using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Favoa.Core.Dados;
using Favoa.Core.Financas;
using Favoa.Core.Fitness;
using Favoa.Core.Projetos;
using Favoa.Core.Rotina;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Favoa.Data;

public sealed class DadosFavoa(IDbContextFactory<FavoaDbContext> fabrica, OpcoesFavoa opcoes) : IDadosFavoa
{
    private const string Marca = "favoa";
    private const int VersaoFormato = 9; // 2 = Fitness; 3 = recorrências, lembretes, orçamento por categoria; 4 = pessoas da casa; 5 = fonte dos alimentos (IBGE, Open Food Facts); 6 = projetos; 7 = horário, aviso e responsáveis das tarefas; 8 = gasto calórico (intensidade e duração do treino); 9 = medidas do corpo
    private const int CopiasManuaisMantidas = 10;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly CultureInfo PtBr = new("pt-BR");

    // ---------- Informações e backup ----------

    private const string MensagemVariasFamilias =
        "Importar e começar do zero valem para a base inteira, e aqui há mais de uma família. Para não mexer nos dados dos outros, isso fica indisponível; a cópia por família chega com a publicação na nuvem.";

    /// <summary>Id da família quando a base tem uma só (instalação doméstica); nulo se há zero ou várias.</summary>
    private static async Task<Guid?> FamiliaUnicaAsync(FavoaDbContext db, CancellationToken ct)
    {
        var ids = await db.Familias.AsNoTracking().Select(f => f.Id).Take(2).ToListAsync(ct);
        return ids.Count == 1 ? ids[0] : null;
    }

    public async Task<bool> BancoDeUmaFamiliaAsync(CancellationToken ct = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);
        return await FamiliaUnicaAsync(db, ct) is not null;
    }

    public async Task<InformacoesDados> ObterInformacoesAsync(CancellationToken ct = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);

        var arquivo = new FileInfo(opcoes.CaminhoBanco);
        var backups = Directory.Exists(opcoes.CaminhoBackups)
            ? new DirectoryInfo(opcoes.CaminhoBackups).GetFiles("*.db")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(20)
                .Select(f => new BackupInfo(f.Name, f.LastWriteTime, f.Length))
                .ToList()
            : [];

        return new InformacoesDados(
            opcoes.CaminhoBanco,
            arquivo.Exists ? arquivo.Length : 0,
            opcoes.CaminhoBackups,
            backups,
            await Contar(db, ct));
    }

    public async Task<ResultadoImportacao> ComecarDoZeroAsync(CancellationToken ct = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct); // sem pessoa: enxerga os registros de todos
        if (await FamiliaUnicaAsync(db, ct) is null) return new ResultadoImportacao(false, MensagemVariasFamilias, null);
        var backup = await CopiarBanco(db, "antes-de-zerar", ct);

        await using var transacao = await db.Database.BeginTransactionAsync(ct);
        var lancamentos = await db.Lancamentos.ExecuteDeleteAsync(ct);
        await db.Recorrencias.ExecuteDeleteAsync(ct);
        await db.ResponsaveisTarefa.ExecuteDeleteAsync(ct);
        var tarefas = await db.Tarefas.ExecuteDeleteAsync(ct);
        await db.RegistrosHabito.ExecuteDeleteAsync(ct);
        var habitos = await db.Habitos.ExecuteDeleteAsync(ct);
        await db.RegistrosAgua.ExecuteDeleteAsync(ct);
        await db.ExerciciosRealizados.ExecuteDeleteAsync(ct);
        var treinos = await db.Sessoes.ExecuteDeleteAsync(ct);
        await db.Consumos.ExecuteDeleteAsync(ct);
        await db.Pesos.ExecuteDeleteAsync(ct);
        await db.Medidas.ExecuteDeleteAsync(ct);
        await db.Lembretes.ExecuteDeleteAsync(ct);
        await db.ItensProjeto.ExecuteDeleteAsync(ct);
        await db.Projetos.ExecuteDeleteAsync(ct);
        await transacao.CommitAsync(ct);

        return new ResultadoImportacao(true,
            $"Pronto, tudo zerado: {lancamentos} lançamento(s), {tarefas} tarefa(s), {habitos} hábito(s) e {treinos} treino(s) apagados, além de água, refeições, peso, recorrências, lembretes e projetos. "
            + $"Se precisar voltar, o backup {Path.GetFileName(backup)} tem tudo como estava.",
            backup);
    }

    public async Task<string> FazerBackupAgoraAsync(CancellationToken ct = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);
        return await CopiarBanco(db, "manual", ct);
    }

    /// <summary>
    /// Copia o banco pela API de backup do SQLite (consistente mesmo com o app aberto).
    /// O nome não começa com "favoa-" para não entrar na rotação do backup diário.
    /// </summary>
    private async Task<string> CopiarBanco(FavoaDbContext db, string prefixo, CancellationToken ct)
    {
        Directory.CreateDirectory(opcoes.CaminhoBackups);
        var destino = Path.Combine(opcoes.CaminhoBackups, $"{prefixo}-{DateTime.Now:yyyy-MM-dd-HHmmss}.db");

        var conexao = (SqliteConnection)db.Database.GetDbConnection();
        var abriAqui = conexao.State != System.Data.ConnectionState.Open;
        if (abriAqui) await conexao.OpenAsync(ct);
        try
        {
            using var copia = new SqliteConnection($"Data Source={destino};Pooling=False");
            copia.Open();
            conexao.BackupDatabase(copia);
        }
        finally
        {
            if (abriAqui) await conexao.CloseAsync();
        }

        foreach (var antigo in Directory.GetFiles(opcoes.CaminhoBackups, $"{prefixo}-*.db")
                     .OrderDescending()
                     .Skip(CopiasManuaisMantidas))
        {
            File.Delete(antigo);
        }

        return destino;
    }

    // ---------- Exportar ----------

    public async Task<ArquivoGerado> ExportarJsonAsync(CancellationToken ct = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);

        var pacote = new PacoteJson(
            Marca,
            VersaoFormato,
            DateTime.UtcNow,
            await db.Contas.AsNoTracking().OrderBy(x => x.Nome).Select(x => new ContaJson(x.Id, x.Nome, x.Ativa, x.CriadoEm)).ToListAsync(ct),
            await db.Categorias.AsNoTracking().OrderBy(x => x.Nome).Select(x => new CategoriaJson(x.Id, x.Nome, x.Tipo, x.CriadoEm) { OrcamentoCentavos = x.OrcamentoCentavos }).ToListAsync(ct),
            await db.Lancamentos.AsNoTracking().OrderBy(x => x.Data).ThenBy(x => x.CriadoEm)
                .Select(x => new LancamentoJson(x.Id, x.ContaId, x.CategoriaId, x.Tipo, x.ValorCentavos, x.Data, x.Descricao, x.CriadoEm) { RecorrenciaId = x.RecorrenciaId, PessoaId = x.PessoaId, ProjetoId = x.ProjetoId }).ToListAsync(ct),
            await db.Tarefas.AsNoTracking().OrderBy(x => x.CriadoEm)
                .Select(x => new TarefaJson(x.Id, x.Titulo, x.Prazo, x.ConcluidaEm, x.CriadoEm)
                {
                    ProjetoId = x.ProjetoId, Hora = x.Hora, AvisoMinutos = x.AvisoMinutos, AvisoEnviadoEm = x.AvisoEnviadoEm,
                    Responsaveis = x.Responsaveis.Select(r => r.PessoaId).ToList(),
                }).ToListAsync(ct),
            await db.Habitos.AsNoTracking().OrderBy(x => x.Nome).Select(x => new HabitoJson(x.Id, x.Nome, x.Ativo, x.CriadoEm) { PessoaId = x.PessoaId }).ToListAsync(ct),
            await db.RegistrosHabito.AsNoTracking().OrderBy(x => x.Data)
                .Select(x => new RegistroHabitoJson(x.Id, x.HabitoId, x.Data, x.CriadoEm)).ToListAsync(ct),
            await db.RegistrosAgua.AsNoTracking().OrderBy(x => x.Data).ThenBy(x => x.CriadoEm)
                .Select(x => new AguaJson(x.Id, x.Mililitros, x.Data, x.CriadoEm) { PessoaId = x.PessoaId }).ToListAsync(ct),
            (await db.Configuracoes.AsNoTracking().OrderBy(x => x.Chave).ToListAsync(ct))
                .Where(x => !AcessoServico.EhChaveLocal(x.Chave)) // PIN e chaves de notificação são deste computador: não saem no arquivo
                .Select(x => new ConfiguracaoJson(x.Chave, x.Valor)).ToList())
        {
            Exercicios = await db.Exercicios.AsNoTracking().OrderBy(x => x.Nome)
                .Select(x => new ExercicioJson(x.Id, x.Nome, x.Grupo, x.Tipo, x.Ativo, x.CriadoEm)).ToListAsync(ct),
            Fichas = (await db.Fichas.AsNoTracking().Include(f => f.Itens).OrderBy(f => f.Ordem).ToListAsync(ct))
                .Select(f => new FichaJson(f.Id, f.Nome, f.Ordem, f.Ativa, f.CriadoEm, f.Itens.OrderBy(i => i.Ordem)
                    .Select(i => new ItemFichaJson(i.Id, i.ExercicioId, i.Ordem, i.Series, i.Repeticoes, i.CargaKg, i.Minutos)).ToList()) { PessoaId = f.PessoaId })
                .ToList(),
            Sessoes = (await db.Sessoes.AsNoTracking().Include(x => x.Exercicios).OrderBy(x => x.Data).ThenBy(x => x.CriadoEm).ToListAsync(ct))
                .Select(x => new SessaoJson(x.Id, x.Data, x.FichaId, x.Observacao, x.CriadoEm, x.Exercicios.OrderBy(e => e.Ordem)
                    .Select(e => new ExercicioRealizadoJson(e.Id, e.ExercicioId, e.Ordem, e.Series, e.Repeticoes, e.CargaKg, e.Minutos, e.DistanciaKm, e.CriadoEm) { Intensidade = (int?)e.Intensidade }).ToList()) { PessoaId = x.PessoaId, MinutosForca = x.MinutosForca, IntensidadeForca = (int?)x.IntensidadeForca })
                .ToList(),
            Alimentos = await db.Alimentos.AsNoTracking().OrderBy(x => x.CodigoTaco == null).ThenBy(x => x.CodigoTaco).ThenBy(x => x.Nome)
                .Select(x => new AlimentoJson(x.Id, x.Nome, x.Grupo, x.Kcal, x.Proteina, x.Carboidrato, x.Gordura, x.Fibra, x.CodigoTaco, x.Personalizado, x.NomePorcao, x.GramasPorcao, x.CriadoEm, x.Fonte, x.CodigoExterno)).ToListAsync(ct),
            // Só as medidas da casa; as do catálogo do IBGE voltam sozinhas na subida do app.
            Porcoes = await db.Porcoes.AsNoTracking().Where(x => x.Origem == OrigemPorcao.Meu).OrderBy(x => x.AlimentoId).ThenBy(x => x.Nome)
                .Select(x => new PorcaoJson(x.Id, x.AlimentoId, x.Nome, x.Gramas, x.Padrao)).ToListAsync(ct),
            Plano = await db.ItensPlano.AsNoTracking().OrderBy(x => x.Refeicao).ThenBy(x => x.Ordem)
                .Select(x => new ItemPlanoJson(x.Id, x.Refeicao, x.AlimentoId, x.Gramas, x.Ordem) { PessoaId = x.PessoaId, Unidade = x.Unidade, Quantidade = x.Quantidade }).ToListAsync(ct),
            Consumos = await db.Consumos.AsNoTracking().OrderBy(x => x.Data).ThenBy(x => x.CriadoEm)
                .Select(x => new ConsumoJson(x.Id, x.Data, x.Refeicao, x.AlimentoId, x.Gramas, x.DoPlano, x.CriadoEm) { PessoaId = x.PessoaId, Unidade = x.Unidade, Quantidade = x.Quantidade }).ToListAsync(ct),
            Pesos = await db.Pesos.AsNoTracking().OrderBy(x => x.Data)
                .Select(x => new PesoJson(x.Id, x.Data, x.Kg, x.CriadoEm) { PessoaId = x.PessoaId }).ToListAsync(ct),
            Medidas = await db.Medidas.AsNoTracking().OrderBy(x => x.Data)
                .Select(x => new MedidaJson(x.Id, x.Data, x.Tipo, x.Cm, x.CriadoEm) { PessoaId = x.PessoaId }).ToListAsync(ct),
            Recorrencias = await db.Recorrencias.AsNoTracking().OrderBy(x => x.DiaDoMes)
                .Select(x => new RecorrenciaJson(x.Id, x.Tipo, x.Descricao, x.ValorCentavos, x.CategoriaId, x.ContaId, x.DiaDoMes, x.Inicio, x.UltimaOcorrencia, x.Ativa, x.CriadoEm)).ToListAsync(ct),
            Lembretes = await db.Lembretes.AsNoTracking().OrderBy(x => x.Hora)
                .Select(x => new LembreteJson(x.Id, x.Titulo, x.Tipo, x.Hora, x.Dias, x.Ativo, x.UltimoDisparo, x.CriadoEm) { PessoaId = x.PessoaId }).ToListAsync(ct),
            Pessoas = await db.Pessoas.AsNoTracking().OrderBy(x => x.Ordem).Select(x => new PessoaJson(x.Id, x.Nome, x.Cor, x.Ordem, x.CriadoEm)).ToListAsync(ct),
            Projetos = await db.Projetos.AsNoTracking().OrderBy(x => x.CriadoEm)
                .Select(x => new ProjetoJson(x.Id, x.Nome, x.Tipo, x.Situacao, x.Inicio, x.Fim, x.OrcamentoCentavos, x.Descricao, x.PessoaId, x.CriadoEm)).ToListAsync(ct),
            ItensProjeto = await db.ItensProjeto.AsNoTracking().OrderBy(x => x.ProjetoId).ThenBy(x => x.Ordem)
                .Select(x => new ItemProjetoJson(x.Id, x.ProjetoId, x.Tipo, x.Texto, x.Feito, x.Data, x.Hora, x.Ordem, x.CriadoEm)).ToListAsync(ct),
        };

        return new ArquivoGerado(
            $"favoa-{DateTime.Now:yyyy-MM-dd-HHmm}.json",
            "application/json",
            JsonSerializer.SerializeToUtf8Bytes(pacote, Json));
    }

    public async Task<ArquivoGerado> ExportarCsvAsync(ModuloCsv modulo, CancellationToken ct = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);
        var linhas = new List<string[]>();
        var pessoas = await db.Pessoas.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Nome, ct);
        string Quem(Guid? id) => id is { } i && pessoas.TryGetValue(i, out var n) ? n : "";

        switch (modulo)
        {
            case ModuloCsv.Lancamentos:
                linhas.Add(["Data", "Tipo", "Valor", "Categoria", "Conta", "Descrição", "Lançado por"]);
                foreach (var l in await db.Lancamentos.AsNoTracking().Include(x => x.Categoria).Include(x => x.Conta)
                             .OrderBy(x => x.Data).ThenBy(x => x.CriadoEm).ToListAsync(ct))
                {
                    linhas.Add([
                        Data(l.Data), l.Tipo == TipoLancamento.Despesa ? "Despesa" : "Receita",
                        ((l.Tipo == TipoLancamento.Despesa ? -l.ValorCentavos : l.ValorCentavos) / 100m).ToString("0.00", PtBr),
                        l.Categoria?.Nome ?? "", l.Conta?.Nome ?? "", l.Descricao, Quem(l.PessoaId),
                    ]);
                }
                break;

            case ModuloCsv.Tarefas:
                linhas.Add(["Tarefa", "Prazo", "Horário", "Aviso", "Responsáveis", "Concluída em", "Criada em"]);
                foreach (var t in await db.Tarefas.AsNoTracking().Include(x => x.Responsaveis).OrderBy(x => x.CriadoEm).ToListAsync(ct))
                    linhas.Add([t.Titulo, Data(t.Prazo), t.Hora?.ToString("HH:mm") ?? "", t.AvisoMinutos is { } av ? AvisoTarefa.Rotulo(av) : "",
                        t.Responsaveis.Count == 0 ? "Casa" : string.Join(", ", t.Responsaveis.Select(r => Quem(r.PessoaId))),
                        Data(t.ConcluidaEm), Data(DateOnly.FromDateTime(t.CriadoEm.ToLocalTime()))]);
                break;

            case ModuloCsv.Habitos:
                linhas.Add(["Data", "Pessoa", "Hábito", "Ativo"]);
                var habitos = await db.Habitos.AsNoTracking().ToDictionaryAsync(h => h.Id, ct);
                foreach (var r in await db.RegistrosHabito.AsNoTracking().OrderBy(x => x.Data).ToListAsync(ct))
                {
                    var h = habitos.GetValueOrDefault(r.HabitoId);
                    linhas.Add([Data(r.Data), Quem(h?.PessoaId), h?.Nome ?? "", h is { Ativo: true } ? "Sim" : "Não"]);
                }
                break;

            case ModuloCsv.Treinos:
                linhas.Add(["Data", "Pessoa", "Ficha", "Exercício", "Grupo", "Séries", "Repetições", "Carga (kg)", "Minutos", "Distância (km)"]);
                foreach (var e in await db.ExerciciosRealizados.AsNoTracking().Include(x => x.Sessao).ThenInclude(x => x!.Ficha).Include(x => x.Exercicio)
                             .ToListAsync(ct))
                {
                    linhas.Add([Data(e.Sessao!.Data), Quem(e.Sessao.PessoaId), e.Sessao.Ficha?.Nome ?? "", e.Exercicio?.Nome ?? "", e.Exercicio?.Grupo ?? "",
                        e.Series?.ToString() ?? "", e.Repeticoes?.ToString() ?? "", e.CargaKg?.ToString("0.##", PtBr) ?? "",
                        e.Minutos?.ToString() ?? "", e.DistanciaKm?.ToString("0.##", PtBr) ?? ""]);
                }
                linhas = [linhas[0], .. linhas.Skip(1).OrderBy(l => DateOnly.ParseExact(l[0], "dd/MM/yyyy", CultureInfo.InvariantCulture))];
                break;

            case ModuloCsv.Refeicoes:
                linhas.Add(["Data", "Pessoa", "Refeição", "Alimento", "Gramas", "Kcal", "Proteína (g)", "Carboidrato (g)", "Gordura (g)", "Do plano"]);
                foreach (var c in await db.Consumos.AsNoTracking().Include(x => x.Alimento).OrderBy(x => x.Data).ThenBy(x => x.Refeicao).ThenBy(x => x.CriadoEm).ToListAsync(ct))
                {
                    var m = Macros.De(c.Alimento!, c.Gramas);
                    linhas.Add([Data(c.Data), Quem(c.PessoaId), Refeicoes.Nome(c.Refeicao), c.Alimento!.Nome, c.Gramas.ToString(),
                        m.Kcal.ToString("0", PtBr), m.Proteina.ToString("0.#", PtBr), m.Carboidrato.ToString("0.#", PtBr), m.Gordura.ToString("0.#", PtBr),
                        c.DoPlano ? "Sim" : "Não"]);
                }
                break;

            case ModuloCsv.Peso:
                linhas.Add(["Data", "Pessoa", "Peso (kg)"]);
                foreach (var p in await db.Pesos.AsNoTracking().OrderBy(x => x.Data).ToListAsync(ct))
                    linhas.Add([Data(p.Data), Quem(p.PessoaId), p.Kg.ToString("0.0", PtBr)]);
                break;

            case ModuloCsv.Agua:
                linhas.Add(["Data", "Pessoa", "Mililitros"]);
                foreach (var a in await db.RegistrosAgua.AsNoTracking().OrderBy(x => x.Data).ThenBy(x => x.CriadoEm).ToListAsync(ct))
                    linhas.Add([Data(a.Data), Quem(a.PessoaId), a.Mililitros.ToString(CultureInfo.InvariantCulture)]);
                break;
        }

        // Ponto e vírgula + BOM: o Excel em português abre direto, com acentos e colunas certas.
        var texto = string.Join("\r\n", linhas.Select(l => string.Join(';', l.Select(Celula)))) + "\r\n";
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(texto)).ToArray();
        var nome = modulo switch
        {
            ModuloCsv.Lancamentos => "lancamentos",
            ModuloCsv.Tarefas => "tarefas",
            ModuloCsv.Habitos => "habitos",
            ModuloCsv.Treinos => "treinos",
            ModuloCsv.Refeicoes => "refeicoes",
            ModuloCsv.Peso => "peso",
            _ => "agua",
        };
        return new ArquivoGerado($"favoa-{nome}-{DateTime.Now:yyyy-MM-dd}.csv", "text/csv", bytes);

        static string Data(DateOnly? d) => d?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "";

        static string Celula(string valor) =>
            valor.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? $"\"{valor.Replace("\"", "\"\"")}\"" : valor;
    }

    // ---------- Importar ----------

    public Task<AnaliseImportacao> AnalisarImportacaoAsync(byte[] json, CancellationToken ct = default)
    {
        var (pacote, erro) = Ler(json);
        return Task.FromResult(pacote is null
            ? new AnaliseImportacao(false, erro!, null, null)
            : new AnaliseImportacao(true, "Arquivo válido.", Contar(pacote), pacote.ExportadoEm));
    }

    public async Task<ResultadoImportacao> ImportarJsonAsync(byte[] json, CancellationToken ct = default)
    {
        var (pacote, erro) = Ler(json);
        if (pacote is null) return new(false, erro!, null);

        await using var db = await fabrica.CreateDbContextAsync(ct);
        if (await FamiliaUnicaAsync(db, ct) is not { } familiaId) return new(false, MensagemVariasFamilias, null);
        var backup = await CopiarBanco(db, "antes-importar", ct);

        await using var transacao = await db.Database.BeginTransactionAsync(ct);
        try
        {
            // Arquivos da versão 1 não têm Fitness: nesse caso a área Fitness fica como está.
            var temFitness = pacote.Alimentos is not null;
            if (temFitness)
            {
                await db.ExerciciosRealizados.ExecuteDeleteAsync(ct);
                await db.Sessoes.ExecuteDeleteAsync(ct);
                await db.ItensFicha.ExecuteDeleteAsync(ct);
                await db.Fichas.ExecuteDeleteAsync(ct);
                await db.Consumos.ExecuteDeleteAsync(ct);
                await db.ItensPlano.ExecuteDeleteAsync(ct);
                await db.Pesos.ExecuteDeleteAsync(ct);
                await db.Medidas.ExecuteDeleteAsync(ct);
                await db.Exercicios.ExecuteDeleteAsync(ct);
                await db.Porcoes.ExecuteDeleteAsync(ct);
                await db.Alimentos.ExecuteDeleteAsync(ct);
            }

            // Filhos antes dos pais, por causa das chaves estrangeiras.
            await db.RegistrosHabito.ExecuteDeleteAsync(ct);
            await db.Lancamentos.ExecuteDeleteAsync(ct);
            await db.Recorrencias.ExecuteDeleteAsync(ct);
            await db.Lembretes.ExecuteDeleteAsync(ct);
            await db.Habitos.ExecuteDeleteAsync(ct);
            await db.ResponsaveisTarefa.ExecuteDeleteAsync(ct);
            await db.Tarefas.ExecuteDeleteAsync(ct);
            await db.ItensProjeto.ExecuteDeleteAsync(ct);
            await db.Projetos.ExecuteDeleteAsync(ct);
            await db.RegistrosAgua.ExecuteDeleteAsync(ct);
            await db.Categorias.ExecuteDeleteAsync(ct);
            await db.Contas.ExecuteDeleteAsync(ct);
            // O PIN e a versão de acesso ficam: importar dados não muda quem pode entrar.
            await db.Configuracoes.Where(c => !c.Chave.StartsWith("acesso_") && !c.Chave.StartsWith("push_")).ExecuteDeleteAsync(ct);

            // Pessoas: as do arquivo substituem as atuais, mantendo o PIN de quem já existia (o PIN é deste PC).
            // Arquivos de antes das pessoas (versão até 3): tudo fica com a primeira pessoa daqui.
            var pessoasAtuais = await db.Pessoas.ToListAsync(ct);
            // Quem tem login não sai, mesmo que o arquivo não tenha essa pessoa: importar dados não muda quem pode entrar.
            var comLogin = (await db.Usuarios.Select(u => u.PessoaId).ToListAsync(ct)).ToHashSet();
            Guid dono;
            if (pacote.Pessoas is { Count: > 0 } pessoasArquivo)
            {
                var ids = pessoasArquivo.Select(x => x.Id).ToHashSet();
                foreach (var sai in pessoasAtuais.Where(x => !ids.Contains(x.Id) && !comLogin.Contains(x.Id))) db.Pessoas.Remove(sai);
                await db.AssinaturasPush.Where(a => a.PessoaId == null || (!ids.Contains(a.PessoaId.Value) && !comLogin.Contains(a.PessoaId.Value))).ExecuteDeleteAsync(ct);
                foreach (var x in pessoasArquivo)
                {
                    var pessoa = pessoasAtuais.FirstOrDefault(p => p.Id == x.Id);
                    if (pessoa is null) db.Pessoas.Add(pessoa = new Favoa.Core.Pessoas.Pessoa { Id = x.Id });
                    pessoa.FamiliaId = familiaId;
                    pessoa.Nome = x.Nome;
                    pessoa.Cor = Favoa.Core.Pessoas.CoresPessoa.Valida(x.Cor) ? x.Cor : Favoa.Core.Pessoas.CoresPessoa.Padrao[0];
                    pessoa.Ordem = x.Ordem;
                    pessoa.CriadoEm = x.CriadoEm;
                }
                dono = pessoasArquivo.OrderBy(x => x.Ordem).First().Id;
            }
            else dono = pessoasAtuais.OrderBy(x => x.Ordem).ThenBy(x => x.CriadoEm).First().Id;
            Guid? Dono(Guid? id) => id ?? dono;
            var arquivoComPessoas = pacote.Pessoas is not null;

            db.Contas.AddRange(pacote.Contas.Select(x => new Conta { Id = x.Id, Nome = x.Nome, Ativa = x.Ativa, CriadoEm = x.CriadoEm, FamiliaId = familiaId }));
            db.Categorias.AddRange(pacote.Categorias.Select(x => new Categoria { Id = x.Id, Nome = x.Nome, Tipo = x.Tipo, OrcamentoCentavos = x.OrcamentoCentavos, CriadoEm = x.CriadoEm, FamiliaId = familiaId }));
            db.Recorrencias.AddRange((pacote.Recorrencias ?? []).Select(x => new Recorrencia
            {
                Id = x.Id, Tipo = x.Tipo, Descricao = x.Descricao, ValorCentavos = x.ValorCentavos, CategoriaId = x.CategoriaId, ContaId = x.ContaId,
                DiaDoMes = x.DiaDoMes, Inicio = x.Inicio, UltimaOcorrencia = x.UltimaOcorrencia, Ativa = x.Ativa, CriadoEm = x.CriadoEm, FamiliaId = familiaId,
            }));
            db.Lembretes.AddRange((pacote.Lembretes ?? []).Select(x => new Lembrete
            {
                Id = x.Id, Titulo = x.Titulo, Tipo = x.Tipo, Hora = x.Hora, Dias = x.Dias, Ativo = x.Ativo, UltimoDisparo = x.UltimoDisparo, CriadoEm = x.CriadoEm, PessoaId = Dono(x.PessoaId),
            }));
            db.Lancamentos.AddRange(pacote.Lancamentos.Select(x => new Lancamento
            {
                Id = x.Id, ContaId = x.ContaId, CategoriaId = x.CategoriaId, Tipo = x.Tipo,
                ValorCentavos = x.ValorCentavos, Data = x.Data, Descricao = x.Descricao ?? "", RecorrenciaId = x.RecorrenciaId, CriadoEm = x.CriadoEm, ProjetoId = x.ProjetoId, FamiliaId = familiaId,
                PessoaId = arquivoComPessoas || x.RecorrenciaId is not null ? x.PessoaId : dono,
            }));
            var idsPessoasFinais = (pacote.Pessoas is { Count: > 0 } pa ? pa.Select(x => x.Id) : pessoasAtuais.Select(x => x.Id)).ToHashSet();
            db.Tarefas.AddRange(pacote.Tarefas.Select(x => new Tarefa
            {
                Id = x.Id, Titulo = x.Titulo, Prazo = x.Prazo, ConcluidaEm = x.ConcluidaEm, CriadoEm = x.CriadoEm, ProjetoId = x.ProjetoId, FamiliaId = familiaId,
                Hora = x.Prazo is null ? null : x.Hora,
                AvisoMinutos = x.Prazo is null || x.Hora is null || x.AvisoMinutos is < 0 or > AvisoTarefa.MaximoMinutos ? null : x.AvisoMinutos,
                AvisoEnviadoEm = x.AvisoEnviadoEm,
                Responsaveis = (x.Responsaveis ?? []).Distinct().Where(idsPessoasFinais.Contains)
                    .Select(id => new TarefaResponsavel { TarefaId = x.Id, PessoaId = id }).ToList(),
            }));
            db.Projetos.AddRange((pacote.Projetos ?? []).Select(x => new Projeto
            {
                Id = x.Id, Nome = x.Nome, Tipo = x.Tipo, Situacao = x.Situacao, Inicio = x.Inicio, Fim = x.Fim, OrcamentoCentavos = x.OrcamentoCentavos,
                Descricao = x.Descricao ?? "", PessoaId = x.PessoaId is null ? null : Dono(x.PessoaId), CriadoEm = x.CriadoEm, FamiliaId = familiaId,
            }));
            db.ItensProjeto.AddRange((pacote.ItensProjeto ?? []).Select(x => new ItemProjeto
            {
                Id = x.Id, ProjetoId = x.ProjetoId, Tipo = x.Tipo, Texto = x.Texto, Feito = x.Feito, Data = x.Data, Hora = x.Hora, Ordem = x.Ordem, CriadoEm = x.CriadoEm,
            }));
            db.Habitos.AddRange(pacote.Habitos.Select(x => new Habito { Id = x.Id, Nome = x.Nome, Ativo = x.Ativo, CriadoEm = x.CriadoEm, PessoaId = Dono(x.PessoaId) }));
            db.RegistrosHabito.AddRange(pacote.RegistrosHabito.Select(x => new RegistroHabito { Id = x.Id, HabitoId = x.HabitoId, Data = x.Data, CriadoEm = x.CriadoEm }));
            db.RegistrosAgua.AddRange(pacote.Agua.Select(x => new RegistroAgua { Id = x.Id, Mililitros = x.Mililitros, Data = x.Data, CriadoEm = x.CriadoEm, PessoaId = Dono(x.PessoaId) }));
            db.Configuracoes.AddRange(pacote.Configuracoes
                .Where(x => !AcessoServico.EhChaveLocal(x.Chave))
                .Select(x => new Configuracao { Chave = x.Chave, Valor = x.Valor }));

            if (temFitness)
            {
                db.Alimentos.AddRange(pacote.Alimentos!.Select(x => new Alimento
                {
                    Id = x.Id, Nome = x.Nome, Grupo = x.Grupo ?? "", Kcal = x.Kcal, Proteina = x.Proteina, Carboidrato = x.Carboidrato,
                    Gordura = x.Gordura, Fibra = x.Fibra, CodigoTaco = x.CodigoTaco, Personalizado = x.Personalizado,
                    NomePorcao = x.NomePorcao, GramasPorcao = x.GramasPorcao, CriadoEm = x.CriadoEm, Fonte = x.Fonte, CodigoExterno = x.CodigoExterno,
                    // TACO e IBGE são catálogo comum (sem família); o resto é da casa.
                    FamiliaId = x.CodigoTaco is not null || x.Fonte == FontesAlimento.Ibge || x.Fonte == FontesAlimento.Estimativa ? null : familiaId,
                }));
                var idsAlimentos = pacote.Alimentos.Select(a => a.Id).ToHashSet();
                db.Porcoes.AddRange((pacote.Porcoes ?? []).Where(x => idsAlimentos.Contains(x.AlimentoId) && x.Gramas > 0 && !string.IsNullOrWhiteSpace(x.Nome))
                    .Select(x => new PorcaoAlimento { Id = x.Id, AlimentoId = x.AlimentoId, Nome = x.Nome.Trim(), Gramas = x.Gramas, Padrao = x.Padrao, Origem = OrigemPorcao.Meu, FamiliaId = familiaId }));
                db.Exercicios.AddRange((pacote.Exercicios ?? []).Select(x => new Exercicio { Id = x.Id, Nome = x.Nome, Grupo = x.Grupo ?? "", Tipo = x.Tipo, Ativo = x.Ativo, CriadoEm = x.CriadoEm, FamiliaId = familiaId }));
                db.Fichas.AddRange((pacote.Fichas ?? []).Select(f => new FichaTreino
                {
                    Id = f.Id, Nome = f.Nome, Ordem = f.Ordem, Ativa = f.Ativa, CriadoEm = f.CriadoEm, PessoaId = Dono(f.PessoaId),
                    Itens = f.Itens.Select(i => new ItemFicha { Id = i.Id, FichaId = f.Id, ExercicioId = i.ExercicioId, Ordem = i.Ordem, Series = i.Series, Repeticoes = i.Repeticoes, CargaKg = i.CargaKg, Minutos = i.Minutos }).ToList(),
                }));
                db.Sessoes.AddRange((pacote.Sessoes ?? []).Select(x => new SessaoTreino
                {
                    Id = x.Id, Data = x.Data, FichaId = x.FichaId, Observacao = x.Observacao ?? "", CriadoEm = x.CriadoEm, PessoaId = Dono(x.PessoaId),
                    MinutosForca = x.MinutosForca, IntensidadeForca = (Intensidade?)x.IntensidadeForca,
                    Exercicios = x.Exercicios.Select(e => new ExercicioRealizado
                    {
                        Id = e.Id, SessaoId = x.Id, ExercicioId = e.ExercicioId, Ordem = e.Ordem, Series = e.Series, Repeticoes = e.Repeticoes,
                        CargaKg = e.CargaKg, Minutos = e.Minutos, DistanciaKm = e.DistanciaKm, CriadoEm = e.CriadoEm, Intensidade = (Intensidade?)e.Intensidade,
                    }).ToList(),
                }));
                db.ItensPlano.AddRange((pacote.Plano ?? []).Select(x => new ItemPlano { Id = x.Id, Refeicao = x.Refeicao, AlimentoId = x.AlimentoId, Gramas = x.Gramas, Unidade = x.Unidade, Quantidade = x.Quantidade, Ordem = x.Ordem, PessoaId = Dono(x.PessoaId) }));
                db.Consumos.AddRange((pacote.Consumos ?? []).Select(x => new Consumo { Id = x.Id, Data = x.Data, Refeicao = x.Refeicao, AlimentoId = x.AlimentoId, Gramas = x.Gramas, Unidade = x.Unidade, Quantidade = x.Quantidade, DoPlano = x.DoPlano, CriadoEm = x.CriadoEm, PessoaId = Dono(x.PessoaId) }));
                db.Medidas.AddRange((pacote.Medidas ?? []).Select(x => new MedidaCorporal { Id = x.Id, Data = x.Data, Tipo = x.Tipo, Cm = x.Cm, CriadoEm = x.CriadoEm, PessoaId = Dono(x.PessoaId) }));
                db.Pesos.AddRange((pacote.Pesos ?? []).Select(x => new RegistroPeso { Id = x.Id, Data = x.Data, Kg = x.Kg, CriadoEm = x.CriadoEm, PessoaId = Dono(x.PessoaId) }));
            }

            await db.SaveChangesAsync(ct);
            // O catálogo de medidas do IBGE vai junto com os alimentos apagados; volta para os alimentos importados.
            if (temFitness) await InfraestruturaExtensions.CarregarMedidasAsync(db);
            await transacao.CommitAsync(ct);
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            await transacao.RollbackAsync(ct);
            return new(false, "Não deu para importar: o arquivo tem dados inconsistentes. Nada foi alterado.", backup);
        }

        var c = Contar(pacote);
        var fitness = pacote.Alimentos is null ? "" : $", {pacote.Sessoes?.Count ?? 0} treinos, {pacote.Consumos?.Count ?? 0} itens de refeição";
        return new(true, $"Importado: {c.Lancamentos} lançamentos, {c.Tarefas} tarefas, {c.Habitos} hábitos{fitness}.", backup);
    }

    /// <summary>Lê e valida o arquivo inteiro antes de tocar no banco.</summary>
    private static (PacoteJson? Pacote, string? Erro) Ler(byte[] json)
    {
        PacoteJson? p;
        try { p = JsonSerializer.Deserialize<PacoteJson>(json, Json); }
        catch (JsonException) { return (null, "Esse arquivo não é um JSON do Fávoa."); }

        if (p is null || p.App != Marca) return (null, "Esse arquivo não é um JSON do Fávoa.");
        if (p.Versao > VersaoFormato) return (null, "Arquivo de uma versão mais nova do Fávoa. Atualize o app antes de importar.");
        if (p.Contas is null || p.Categorias is null || p.Lancamentos is null || p.Tarefas is null ||
            p.Habitos is null || p.RegistrosHabito is null || p.Agua is null || p.Configuracoes is null)
            return (null, "O arquivo está incompleto.");
        if (p.Contas.Any(x => string.IsNullOrWhiteSpace(x.Nome)) || p.Categorias.Any(x => string.IsNullOrWhiteSpace(x.Nome)) ||
            p.Habitos.Any(x => string.IsNullOrWhiteSpace(x.Nome)) || p.Tarefas.Any(x => string.IsNullOrWhiteSpace(x.Titulo)) ||
            p.Configuracoes.Any(x => string.IsNullOrWhiteSpace(x.Chave) || x.Valor is null))
            return (null, "O arquivo tem itens sem nome.");

        var contas = p.Contas.Select(x => x.Id).ToHashSet();
        var categorias = p.Categorias.Select(x => x.Id).ToHashSet();
        var habitos = p.Habitos.Select(x => x.Id).ToHashSet();

        if (p.Lancamentos.Any(l => !contas.Contains(l.ContaId) || !categorias.Contains(l.CategoriaId)))
            return (null, "Há lançamentos apontando para contas ou categorias que não estão no arquivo.");
        var recorrencias = (p.Recorrencias ?? []).Select(x => x.Id).ToHashSet();
        if ((p.Recorrencias ?? []).Any(r => !contas.Contains(r.ContaId) || !categorias.Contains(r.CategoriaId) || r.ValorCentavos <= 0 || r.DiaDoMes is < 1 or > 31 || string.IsNullOrWhiteSpace(r.Descricao)))
            return (null, "Há recorrências incompletas ou apontando para contas e categorias que não estão no arquivo.");
        if (p.Lancamentos.Any(l => l.RecorrenciaId is { } r && !recorrencias.Contains(r)))
            return (null, "Há lançamentos apontando para recorrências que não estão no arquivo.");
        if ((p.Lembretes ?? []).Any(l => string.IsNullOrWhiteSpace(l.Titulo)))
            return (null, "Há lembretes sem título.");
        if (p.Pessoas is { } pessoasArq)
        {
            if (pessoasArq.Count == 0 || pessoasArq.Any(x => string.IsNullOrWhiteSpace(x.Nome)) || Repetido(pessoasArq.Select(x => x.Nome.Trim().ToUpperInvariant())))
                return (null, "O arquivo tem pessoas sem nome ou repetidas.");
            var idsPessoas = pessoasArq.Select(x => x.Id).ToHashSet();
            var donos = p.Habitos.Select(x => x.PessoaId).Concat(p.Agua.Select(x => x.PessoaId)).Concat((p.Lembretes ?? []).Select(x => x.PessoaId))
                .Concat((p.Fichas ?? []).Select(x => x.PessoaId)).Concat((p.Sessoes ?? []).Select(x => x.PessoaId)).Concat((p.Plano ?? []).Select(x => x.PessoaId))
                .Concat((p.Consumos ?? []).Select(x => x.PessoaId)).Concat((p.Pesos ?? []).Select(x => x.PessoaId)).Concat((p.Medidas ?? []).Select(x => x.PessoaId)).Concat(p.Lancamentos.Select(x => x.PessoaId));
            if (donos.Any(d => d is { } id && !idsPessoas.Contains(id)))
                return (null, "Há registros apontando para pessoas que não estão no arquivo.");
        }
        if (p.RegistrosHabito.Any(r => !habitos.Contains(r.HabitoId)))
            return (null, "Há registros de hábito sem o hábito correspondente.");
        if (p.Lancamentos.Any(l => l.ValorCentavos <= 0) || p.Agua.Any(a => a.Mililitros <= 0))
            return (null, "Há valores zerados ou negativos no arquivo.");
        if (Repetido(p.Contas.Select(x => x.Nome.ToUpperInvariant())) ||
            Repetido(p.Categorias.Select(x => $"{x.Nome.ToUpperInvariant()}|{x.Tipo}")) ||
            Repetido(p.Habitos.Select(x => $"{x.PessoaId}|{x.Nome.ToUpperInvariant()}")) ||
            Repetido(p.RegistrosHabito.Select(x => $"{x.HabitoId}|{x.Data}")))
            return (null, "O arquivo tem nomes repetidos (contas, categorias ou hábitos).");

        if (p.Alimentos is not null)
        {
            var alimentos = p.Alimentos.Select(x => x.Id).ToHashSet();
            var exercicios = (p.Exercicios ?? []).Select(x => x.Id).ToHashSet();
            var fichas = (p.Fichas ?? []).Select(x => x.Id).ToHashSet();
            if ((p.Consumos ?? []).Any(c => !alimentos.Contains(c.AlimentoId) || c.Gramas <= 0) ||
                (p.Plano ?? []).Any(c => !alimentos.Contains(c.AlimentoId) || c.Gramas <= 0))
                return (null, "Há refeições apontando para alimentos que não estão no arquivo.");
            if ((p.Fichas ?? []).SelectMany(f => f.Itens ?? []).Any(i => !exercicios.Contains(i.ExercicioId)) ||
                (p.Sessoes ?? []).SelectMany(s => s.Exercicios ?? []).Any(e => !exercicios.Contains(e.ExercicioId)) ||
                (p.Sessoes ?? []).Any(s => s.FichaId is { } f && !fichas.Contains(f)))
                return (null, "Há treinos apontando para exercícios ou fichas que não estão no arquivo.");
            if (p.Alimentos.Any(a => string.IsNullOrWhiteSpace(a.Nome)) || (p.Exercicios ?? []).Any(e => string.IsNullOrWhiteSpace(e.Nome)) ||
                (p.Fichas ?? []).Any(f => string.IsNullOrWhiteSpace(f.Nome) || f.Itens is null) || (p.Sessoes ?? []).Any(s => s.Exercicios is null))
                return (null, "O arquivo tem itens de Fitness incompletos.");
            if (Repetido((p.Exercicios ?? []).Select(x => x.Nome.ToUpperInvariant())) ||
                Repetido((p.Pesos ?? []).Select(x => $"{x.PessoaId}|{x.Data}")) ||
                Repetido(p.Alimentos.Where(a => a.CodigoTaco != null).Select(a => a.CodigoTaco!.Value.ToString())))
                return (null, "O arquivo tem exercícios, pesos ou alimentos repetidos.");
        }

        return (p, null);

        static bool Repetido(IEnumerable<string> valores) => valores.GroupBy(v => v).Any(g => g.Count() > 1);
    }

    private static ContagemDados Contar(PacoteJson p) =>
        new(p.Lancamentos.Count, p.Contas.Count, p.Categorias.Count, p.Tarefas.Count, p.Habitos.Count, p.RegistrosHabito.Count, p.Agua.Count);

    private static async Task<ContagemDados> Contar(FavoaDbContext db, CancellationToken ct) => new(
        await db.Lancamentos.CountAsync(ct), await db.Contas.CountAsync(ct), await db.Categorias.CountAsync(ct),
        await db.Tarefas.CountAsync(ct), await db.Habitos.CountAsync(ct), await db.RegistrosHabito.CountAsync(ct),
        await db.RegistrosAgua.CountAsync(ct));

    // ---------- Formato do arquivo (versão 1) ----------

    private sealed record PacoteJson(
        string App, int Versao, DateTime ExportadoEm,
        List<ContaJson> Contas, List<CategoriaJson> Categorias, List<LancamentoJson> Lancamentos,
        List<TarefaJson> Tarefas, List<HabitoJson> Habitos, List<RegistroHabitoJson> RegistrosHabito,
        List<AguaJson> Agua, List<ConfiguracaoJson> Configuracoes)
    {
        // Versão 2 (nulos em arquivos da versão 1)
        public List<ExercicioJson>? Exercicios { get; init; }
        public List<FichaJson>? Fichas { get; init; }
        public List<SessaoJson>? Sessoes { get; init; }
        public List<AlimentoJson>? Alimentos { get; init; }
        public List<PorcaoJson>? Porcoes { get; init; }
        public List<ItemPlanoJson>? Plano { get; init; }
        public List<ConsumoJson>? Consumos { get; init; }
        public List<PesoJson>? Pesos { get; init; }
        public List<MedidaJson>? Medidas { get; init; }

        // Versão 3
        public List<RecorrenciaJson>? Recorrencias { get; init; }
        public List<LembreteJson>? Lembretes { get; init; }

        // Versão 4
        public List<PessoaJson>? Pessoas { get; init; }

        // Versão 6
        public List<ProjetoJson>? Projetos { get; init; }
        public List<ItemProjetoJson>? ItensProjeto { get; init; }
    }

    private sealed record ContaJson(Guid Id, string Nome, bool Ativa, DateTime CriadoEm);
    private sealed record CategoriaJson(Guid Id, string Nome, TipoLancamento Tipo, DateTime CriadoEm) { public long? OrcamentoCentavos { get; init; } }
    private sealed record LancamentoJson(Guid Id, Guid ContaId, Guid CategoriaId, TipoLancamento Tipo, long ValorCentavos, DateOnly Data, string? Descricao, DateTime CriadoEm) { public Guid? RecorrenciaId { get; init; } public Guid? PessoaId { get; init; } public Guid? ProjetoId { get; init; } }
    private sealed record PessoaJson(Guid Id, string Nome, string Cor, int Ordem, DateTime CriadoEm);
    private sealed record RecorrenciaJson(Guid Id, TipoLancamento Tipo, string Descricao, long ValorCentavos, Guid CategoriaId, Guid ContaId, int DiaDoMes, DateOnly Inicio, DateOnly? UltimaOcorrencia, bool Ativa, DateTime CriadoEm);
    private sealed record LembreteJson(Guid Id, string Titulo, TipoLembrete Tipo, TimeOnly Hora, DiasSemana Dias, bool Ativo, DateOnly? UltimoDisparo, DateTime CriadoEm) { public Guid? PessoaId { get; init; } }
    private sealed record TarefaJson(Guid Id, string Titulo, DateOnly? Prazo, DateOnly? ConcluidaEm, DateTime CriadoEm)
    {
        public Guid? ProjetoId { get; init; }
        public TimeOnly? Hora { get; init; }
        public int? AvisoMinutos { get; init; }
        public DateTime? AvisoEnviadoEm { get; init; }
        public List<Guid>? Responsaveis { get; init; }
    }
    private sealed record ProjetoJson(Guid Id, string Nome, TipoProjeto Tipo, SituacaoProjeto Situacao, DateOnly? Inicio, DateOnly? Fim, long? OrcamentoCentavos, string? Descricao, Guid? PessoaId, DateTime CriadoEm);
    private sealed record ItemProjetoJson(Guid Id, Guid ProjetoId, TipoItemProjeto Tipo, string Texto, bool Feito, DateOnly? Data, TimeOnly? Hora, int Ordem, DateTime CriadoEm);
    private sealed record HabitoJson(Guid Id, string Nome, bool Ativo, DateTime CriadoEm) { public Guid? PessoaId { get; init; } }
    private sealed record RegistroHabitoJson(Guid Id, Guid HabitoId, DateOnly Data, DateTime CriadoEm);
    private sealed record AguaJson(Guid Id, int Mililitros, DateOnly Data, DateTime CriadoEm) { public Guid? PessoaId { get; init; } }
    private sealed record ConfiguracaoJson(string Chave, string Valor);
    private sealed record ExercicioJson(Guid Id, string Nome, string? Grupo, TipoExercicio Tipo, bool Ativo, DateTime CriadoEm);
    private sealed record FichaJson(Guid Id, string Nome, int Ordem, bool Ativa, DateTime CriadoEm, List<ItemFichaJson> Itens) { public Guid? PessoaId { get; init; } }
    private sealed record ItemFichaJson(Guid Id, Guid ExercicioId, int Ordem, int? Series, int? Repeticoes, decimal? CargaKg, int? Minutos);
    private sealed record SessaoJson(Guid Id, DateOnly Data, Guid? FichaId, string? Observacao, DateTime CriadoEm, List<ExercicioRealizadoJson> Exercicios) { public Guid? PessoaId { get; init; } public int? MinutosForca { get; init; } public int? IntensidadeForca { get; init; } }
    private sealed record ExercicioRealizadoJson(Guid Id, Guid ExercicioId, int Ordem, int? Series, int? Repeticoes, decimal? CargaKg, int? Minutos, decimal? DistanciaKm, DateTime CriadoEm) { public int? Intensidade { get; init; } }
    private sealed record AlimentoJson(Guid Id, string Nome, string? Grupo, double Kcal, double Proteina, double Carboidrato, double Gordura, double Fibra, int? CodigoTaco, bool Personalizado, string? NomePorcao, int? GramasPorcao, DateTime CriadoEm,
        string? Fonte = null, string? CodigoExterno = null);
    private sealed record ItemPlanoJson(Guid Id, Refeicao Refeicao, Guid AlimentoId, int Gramas, int Ordem) { public Guid? PessoaId { get; init; } public string? Unidade { get; init; } public double? Quantidade { get; init; } }
    private sealed record ConsumoJson(Guid Id, DateOnly Data, Refeicao Refeicao, Guid AlimentoId, int Gramas, bool DoPlano, DateTime CriadoEm) { public Guid? PessoaId { get; init; } public string? Unidade { get; init; } public double? Quantidade { get; init; } }

    private sealed record PorcaoJson(Guid Id, Guid AlimentoId, string Nome, double Gramas, bool Padrao);
    private sealed record MedidaJson(Guid Id, DateOnly Data, string Tipo, decimal Cm, DateTime CriadoEm) { public Guid? PessoaId { get; init; } }
    private sealed record PesoJson(Guid Id, DateOnly Data, decimal Kg, DateTime CriadoEm) { public Guid? PessoaId { get; init; } }
}
