using Favoa.Core.Acesso;
using Favoa.Core.Financas;
using Favoa.Core.Fitness;
using Favoa.Core.Pessoas;
using Favoa.Core.Projetos;
using Favoa.Core.Rotina;
using Microsoft.EntityFrameworkCore;

namespace Favoa.Data;

public sealed class FavoaDbContext(DbContextOptions<FavoaDbContext> options) : DbContext(options)
{
    /// <summary>
    /// Pessoa que está usando. Com valor, as tabelas pessoais (treino, dieta, peso, hábitos, água, lembretes)
    /// só mostram os registros dela, e o que for gravado sai com ela como dona. Nulo = vê a casa toda
    /// (exportar, agendador, cadastros da casa).
    /// </summary>
    public Guid? PessoaAtual { get; set; }

    /// <summary>
    /// Família (tenant) de quem está usando. Com valor, só aparecem os registros da casa dela (contas, lançamentos, tarefas,
    /// projetos, exercícios, alimentos próprios e as pessoas), e o que for gravado sai com ela como dona.
    /// Nulo = rotinas internas que enxergam tudo (exportar, agendador, testes).
    /// </summary>
    public Guid? FamiliaAtual { get; set; }

    public DbSet<Familia> Familias => Set<Familia>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Convite> Convites => Set<Convite>();
    public DbSet<Pessoa> Pessoas => Set<Pessoa>();
    public DbSet<Projeto> Projetos => Set<Projeto>();
    public DbSet<ItemProjeto> ItensProjeto => Set<ItemProjeto>();
    public DbSet<Conta> Contas => Set<Conta>();
    public DbSet<Categoria> Categorias => Set<Categoria>();
    public DbSet<Lancamento> Lancamentos => Set<Lancamento>();
    public DbSet<Tarefa> Tarefas => Set<Tarefa>();
    public DbSet<TarefaResponsavel> ResponsaveisTarefa => Set<TarefaResponsavel>();
    public DbSet<Habito> Habitos => Set<Habito>();
    public DbSet<RegistroHabito> RegistrosHabito => Set<RegistroHabito>();
    public DbSet<RegistroAgua> RegistrosAgua => Set<RegistroAgua>();
    public DbSet<Configuracao> Configuracoes => Set<Configuracao>();
    public DbSet<Recorrencia> Recorrencias => Set<Recorrencia>();
    public DbSet<Lembrete> Lembretes => Set<Lembrete>();
    public DbSet<AssinaturaPush> AssinaturasPush => Set<AssinaturaPush>();

    public DbSet<Exercicio> Exercicios => Set<Exercicio>();
    public DbSet<FichaTreino> Fichas => Set<FichaTreino>();
    public DbSet<ItemFicha> ItensFicha => Set<ItemFicha>();
    public DbSet<SessaoTreino> Sessoes => Set<SessaoTreino>();
    public DbSet<ExercicioRealizado> ExerciciosRealizados => Set<ExercicioRealizado>();
    public DbSet<Alimento> Alimentos => Set<Alimento>();
    public DbSet<PorcaoAlimento> Porcoes => Set<PorcaoAlimento>();
    public DbSet<ItemPlano> ItensPlano => Set<ItemPlano>();
    public DbSet<Consumo> Consumos => Set<Consumo>();
    public DbSet<RegistroPeso> Pesos => Set<RegistroPeso>();
    public DbSet<MedidaCorporal> Medidas => Set<MedidaCorporal>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Prefixo por módulo: fin_, rot_, cfg_. Facilita separar em módulos no futuro.
        b.Entity<Conta>(e =>
        {
            e.ToTable("fin_conta");
            e.Property(x => x.Nome).HasMaxLength(80).IsRequired();
            e.HasIndex(x => new { x.FamiliaId, x.Nome }).IsUnique();
        });

        b.Entity<Categoria>(e =>
        {
            e.ToTable("fin_categoria");
            e.Property(x => x.Nome).HasMaxLength(80).IsRequired();
            e.HasIndex(x => new { x.FamiliaId, x.Nome, x.Tipo }).IsUnique();
        });

        b.Entity<Lancamento>(e =>
        {
            e.ToTable("fin_lancamento");
            e.Property(x => x.Descricao).HasMaxLength(300);
            e.HasIndex(x => x.Data);
            e.HasIndex(x => x.ProjetoId);
            e.HasOne(x => x.Conta).WithMany().HasForeignKey(x => x.ContaId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Categoria).WithMany().HasForeignKey(x => x.CategoriaId).OnDelete(DeleteBehavior.Restrict);
            // Sem chave estrangeira de propósito: no SQLite, criar uma em tabela existente exige recriar a tabela.
            // Ao excluir uma recorrência, CadastrosServico limpa a ligação dos lançamentos.
            e.HasIndex(x => x.RecorrenciaId);
        });

        b.Entity<Recorrencia>(e =>
        {
            e.ToTable("fin_recorrencia");
            e.Property(x => x.Descricao).HasMaxLength(300).IsRequired();
            e.HasOne(x => x.Conta).WithMany().HasForeignKey(x => x.ContaId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Categoria).WithMany().HasForeignKey(x => x.CategoriaId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Lembrete>(e =>
        {
            e.ToTable("rot_lembrete");
            e.Property(x => x.Titulo).HasMaxLength(120).IsRequired();
            e.Property(x => x.UltimoResultado).HasMaxLength(300);
        });

        b.Entity<AssinaturaPush>(e =>
        {
            e.ToTable("cfg_push_assinatura");
            e.Property(x => x.Endpoint).HasMaxLength(1000).IsRequired();
            e.Property(x => x.P256dh).HasMaxLength(200).IsRequired();
            e.Property(x => x.Auth).HasMaxLength(100).IsRequired();
            e.Property(x => x.Aparelho).HasMaxLength(120);
            e.HasIndex(x => x.Endpoint).IsUnique();
        });

        b.Entity<Tarefa>(e =>
        {
            e.ToTable("rot_tarefa");
            e.Property(x => x.Titulo).HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.Prazo);
            e.HasIndex(x => x.ProjetoId);
            e.HasMany(x => x.Responsaveis).WithOne().HasForeignKey(x => x.TarefaId).OnDelete(DeleteBehavior.Cascade);
        });

        // Tabela nova (não recria rot_tarefa): a chave estrangeira fica só aqui.
        b.Entity<TarefaResponsavel>(e =>
        {
            e.ToTable("rot_tarefa_responsavel");
            e.HasKey(x => new { x.TarefaId, x.PessoaId });
            e.HasIndex(x => x.PessoaId);
        });

        b.Entity<Habito>(e =>
        {
            e.ToTable("rot_habito");
            e.Property(x => x.Nome).HasMaxLength(80).IsRequired();
            e.HasIndex(x => new { x.PessoaId, x.Nome }).IsUnique();
        });

        b.Entity<RegistroHabito>(e =>
        {
            e.ToTable("rot_habito_registro");
            e.HasIndex(x => new { x.HabitoId, x.Data }).IsUnique();
            e.HasOne(x => x.Habito).WithMany().HasForeignKey(x => x.HabitoId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<RegistroAgua>(e =>
        {
            e.ToTable("rot_agua");
            e.HasIndex(x => x.Data);
        });

        b.Entity<Configuracao>(e =>
        {
            e.ToTable("cfg_configuracao");
            e.HasKey(x => x.Chave);
            e.Property(x => x.Chave).HasMaxLength(80);
        });

        // ---------- Fitness (fit_) ----------
        b.Entity<Exercicio>(e =>
        {
            e.ToTable("fit_exercicio");
            e.Property(x => x.Nome).HasMaxLength(80).IsRequired();
            e.Property(x => x.Grupo).HasMaxLength(40);
            e.HasIndex(x => new { x.FamiliaId, x.Nome }).IsUnique();
        });

        b.Entity<FichaTreino>(e =>
        {
            e.ToTable("fit_ficha");
            e.Property(x => x.Nome).HasMaxLength(80).IsRequired();
            e.HasMany(x => x.Itens).WithOne().HasForeignKey(x => x.FichaId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ItemFicha>(e =>
        {
            e.ToTable("fit_ficha_item");
            e.HasOne(x => x.Exercicio).WithMany().HasForeignKey(x => x.ExercicioId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<SessaoTreino>(e =>
        {
            e.ToTable("fit_sessao");
            e.Property(x => x.Observacao).HasMaxLength(500);
            e.HasIndex(x => x.Data);
            e.HasOne(x => x.Ficha).WithMany().HasForeignKey(x => x.FichaId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(x => x.Exercicios).WithOne(x => x.Sessao).HasForeignKey(x => x.SessaoId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ExercicioRealizado>(e =>
        {
            e.ToTable("fit_sessao_exercicio");
            e.Ignore(x => x.Volume);
            e.HasOne(x => x.Exercicio).WithMany().HasForeignKey(x => x.ExercicioId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Alimento>(e =>
        {
            e.ToTable("fit_alimento");
            e.Property(x => x.Nome).HasMaxLength(120).IsRequired();
            e.Property(x => x.Grupo).HasMaxLength(80);
            e.Property(x => x.NomePorcao).HasMaxLength(40);
            e.HasIndex(x => x.Nome);
            e.HasIndex(x => x.CodigoTaco).IsUnique();
            e.Property(x => x.Fonte).HasMaxLength(30);
            e.Property(x => x.CodigoExterno).HasMaxLength(40);
            e.HasIndex(x => new { x.FamiliaId, x.Fonte, x.CodigoExterno }).IsUnique();
        });

        b.Entity<PorcaoAlimento>(e =>
        {
            e.ToTable("fit_alimento_porcao");
            e.Property(x => x.Nome).HasMaxLength(60).IsRequired();
            e.Property(x => x.Origem).HasMaxLength(10).IsRequired();
            e.HasIndex(x => new { x.AlimentoId, x.FamiliaId, x.Nome }).IsUnique();
            e.HasIndex(x => x.FamiliaId);
            e.HasOne<Alimento>().WithMany().HasForeignKey(x => x.AlimentoId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ItemPlano>(e =>
        {
            e.ToTable("fit_plano_item");
            e.Property(x => x.Unidade).HasMaxLength(60);
            e.HasIndex(x => x.Refeicao);
            e.HasOne(x => x.Alimento).WithMany().HasForeignKey(x => x.AlimentoId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Consumo>(e =>
        {
            e.ToTable("fit_consumo");
            e.Property(x => x.Unidade).HasMaxLength(60);
            e.HasIndex(x => x.Data);
            e.HasOne(x => x.Alimento).WithMany().HasForeignKey(x => x.AlimentoId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<RegistroPeso>(e =>
        {
            e.ToTable("fit_peso");
            e.HasIndex(x => new { x.PessoaId, x.Data }).IsUnique();
        });

        b.Entity<MedidaCorporal>(e =>
        {
            e.ToTable("fit_medida");
            e.Property(x => x.Tipo).HasMaxLength(40);
            e.HasIndex(x => new { x.PessoaId, x.Tipo, x.Data }).IsUnique();
        });

        // ---------- Pessoas ----------
        b.Entity<Pessoa>(e =>
        {
            e.ToTable("cfg_pessoa");
            e.Property(x => x.Nome).HasMaxLength(40).IsRequired();
            e.Property(x => x.Cor).HasMaxLength(7).IsRequired();
            e.Property(x => x.PinHash).HasMaxLength(200);
        });

        // ---------- Famílias e acesso ----------
        b.Entity<Familia>(e =>
        {
            e.ToTable("cfg_familia");
            e.Property(x => x.Nome).HasMaxLength(60).IsRequired();
        });
        b.Entity<Usuario>(e =>
        {
            e.ToTable("cfg_usuario");
            e.Property(x => x.Email).HasMaxLength(254).IsRequired();
            e.Property(x => x.EmailNormalizado).HasMaxLength(254).IsRequired();
            e.Property(x => x.SenhaHash).HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.EmailNormalizado).IsUnique();
            e.HasIndex(x => x.FamiliaId);
            e.HasIndex(x => x.PessoaId).IsUnique();
        });
        b.Entity<Convite>(e =>
        {
            e.ToTable("cfg_convite");
            e.Property(x => x.CodigoHash).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.CodigoHash).IsUnique();
            e.HasIndex(x => x.FamiliaId);
        });

        // Tenant: cada família só enxerga o que é dela. Sem FamiliaAtual (rotinas internas) enxerga tudo.
        b.Entity<Pessoa>().HasQueryFilter(x => FamiliaAtual == null || x.FamiliaId == FamiliaAtual);
        b.Entity<Conta>().HasQueryFilter(x => FamiliaAtual == null || x.FamiliaId == FamiliaAtual);
        b.Entity<Categoria>().HasQueryFilter(x => FamiliaAtual == null || x.FamiliaId == FamiliaAtual);
        b.Entity<Lancamento>().HasQueryFilter(x => FamiliaAtual == null || x.FamiliaId == FamiliaAtual);
        b.Entity<Recorrencia>().HasQueryFilter(x => FamiliaAtual == null || x.FamiliaId == FamiliaAtual);
        b.Entity<Tarefa>().HasQueryFilter(x => FamiliaAtual == null || x.FamiliaId == FamiliaAtual);
        b.Entity<Exercicio>().HasQueryFilter(x => FamiliaAtual == null || x.FamiliaId == FamiliaAtual);
        // Alimentos: a TACO e a tabela do IBGE (sem família) valem para todos; os cadastrados à mão ou pela internet são da casa.
        b.Entity<Alimento>().HasQueryFilter(x => FamiliaAtual == null || x.FamiliaId == null || x.FamiliaId == FamiliaAtual);
        // Medidas: as do catálogo (sem família) valem para todos; as ajustadas ou criadas pela casa, só para ela.
        b.Entity<PorcaoAlimento>().HasQueryFilter(x => FamiliaAtual == null || x.FamiliaId == null || x.FamiliaId == FamiliaAtual);

        // Sem chave estrangeira para cfg_pessoa de propósito: no SQLite, criar FK em tabela existente
        // obriga a recriar a tabela. A pessoa é preenchida pelo contexto (SaveChanges) e filtrada aqui.
        b.Entity<Habito>().HasQueryFilter(x => PessoaAtual == null || x.PessoaId == PessoaAtual);
        b.Entity<RegistroHabito>().HasQueryFilter(x => PessoaAtual == null || x.Habito!.PessoaId == PessoaAtual);
        b.Entity<RegistroAgua>().HasQueryFilter(x => PessoaAtual == null || x.PessoaId == PessoaAtual);
        b.Entity<Lembrete>().HasQueryFilter(x => PessoaAtual == null || x.PessoaId == PessoaAtual);
        b.Entity<AssinaturaPush>().HasQueryFilter(x => PessoaAtual == null || x.PessoaId == PessoaAtual);
        b.Entity<FichaTreino>().HasQueryFilter(x => PessoaAtual == null || x.PessoaId == PessoaAtual);
        b.Entity<SessaoTreino>().HasQueryFilter(x => PessoaAtual == null || x.PessoaId == PessoaAtual);
        b.Entity<ExercicioRealizado>().HasQueryFilter(x => PessoaAtual == null || x.Sessao!.PessoaId == PessoaAtual);
        b.Entity<ItemPlano>().HasQueryFilter(x => PessoaAtual == null || x.PessoaId == PessoaAtual);
        b.Entity<Consumo>().HasQueryFilter(x => PessoaAtual == null || x.PessoaId == PessoaAtual);
        b.Entity<RegistroPeso>().HasQueryFilter(x => PessoaAtual == null || x.PessoaId == PessoaAtual);
        b.Entity<MedidaCorporal>().HasQueryFilter(x => PessoaAtual == null || x.PessoaId == PessoaAtual);
        // Projeto: os da casa (sem dono) para todos, os pessoais só para o dono.
        b.Entity<Projeto>().HasQueryFilter(x => (FamiliaAtual == null || x.FamiliaId == FamiliaAtual)
            && (PessoaAtual == null || x.PessoaId == null || x.PessoaId == PessoaAtual));

        // Filhos sem dono próprio herdam o filtro do pai: sem isso, quem soubesse o id alcançaria o item de outra casa.
        b.Entity<ItemProjeto>().HasQueryFilter(x => Projetos.Any(p => p.Id == x.ProjetoId));
        b.Entity<ItemFicha>().HasQueryFilter(x => Fichas.Any(f => f.Id == x.FichaId));
        b.Entity<TarefaResponsavel>().HasQueryFilter(x => Tarefas.Any(t => t.Id == x.TarefaId));

        b.Entity<Projeto>(e =>
        {
            e.ToTable("prj_projeto");
            e.Property(x => x.Nome).HasMaxLength(80).IsRequired();
            e.Property(x => x.Descricao).HasMaxLength(2000);
            e.HasIndex(x => x.PessoaId);
        });
        b.Entity<ItemProjeto>(e =>
        {
            e.ToTable("prj_item");
            e.Property(x => x.Texto).HasMaxLength(1000).IsRequired();
            e.HasIndex(x => x.ProjetoId);
        });
        foreach (var tipo in new[] { typeof(Habito), typeof(RegistroAgua), typeof(Lembrete), typeof(AssinaturaPush), typeof(FichaTreino),
                     typeof(SessaoTreino), typeof(ItemPlano), typeof(Consumo), typeof(Lancamento) })
            b.Entity(tipo).HasIndex(nameof(IComDono.PessoaId));
        foreach (var tipo in new[] { typeof(Pessoa), typeof(Conta), typeof(Categoria), typeof(Lancamento), typeof(Recorrencia), typeof(Tarefa),
                     typeof(Projeto), typeof(Exercicio), typeof(Alimento) })
            b.Entity(tipo).HasIndex(nameof(IDaFamilia.FamiliaId));
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PreencherDono();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        PreencherDono();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>Registro novo sem dono fica com a pessoa atual (e lançamento, com quem lançou).</summary>
    private void PreencherDono()
    {
        if (FamiliaAtual is { } familia)
        {
            foreach (var e in ChangeTracker.Entries<IDaFamilia>())
                if (e.State == EntityState.Added && e.Entity.FamiliaId is null)
                    e.Entity.FamiliaId = familia;
            foreach (var e in ChangeTracker.Entries<Pessoa>())
                if (e.State == EntityState.Added && e.Entity.FamiliaId is null)
                    e.Entity.FamiliaId = familia;
        }

        if (PessoaAtual is not { } pessoa) return;
        foreach (var e in ChangeTracker.Entries<IComDono>())
            if (e.State == EntityState.Added && e.Entity.PessoaId is null)
                e.Entity.PessoaId = pessoa;
    }
}
