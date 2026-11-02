using Microsoft.EntityFrameworkCore;
using QuoteSnap.Api.Domain;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.Data;

public sealed class QuoteSnapDbContext(DbContextOptions<QuoteSnapDbContext> options) : DbContext(options)
{
    public DbSet<QuoteRequest> QuoteRequests => Set<QuoteRequest>();

    public DbSet<QuoteResult> QuoteResults => Set<QuoteResult>();

    public DbSet<FyersSessionRecord> FyersSessions => Set<FyersSessionRecord>();

    public DbSet<MutualFundNav> MutualFundNavs => Set<MutualFundNav>();

    public DbSet<MyFundRecord> MyFunds => Set<MyFundRecord>();

    public DbSet<FundHoldingRecord> FundHoldings => Set<FundHoldingRecord>();

    public DbSet<HoldingFetchRecord> HoldingFetches => Set<HoldingFetchRecord>();

    public DbSet<HoldingFetchFundRecord> HoldingFetchFunds => Set<HoldingFetchFundRecord>();

    public DbSet<FyersSymbol> FyersSymbols => Set<FyersSymbol>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<QuoteRequestStatus>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<QuoteResultStatus>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<HoldingFetchStatus>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<HoldingFetchFundStatus>().HaveConversion<string>().HaveMaxLength(32);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<QuoteRequest>(entity =>
        {
            entity.ToTable("QuoteRequests");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Error).HasMaxLength(1000);
            entity.HasIndex(r => r.CreatedAt);
            entity.HasIndex(r => r.Status);
            entity.HasMany(r => r.Results)
                .WithOne()
                .HasForeignKey(r => r.RequestId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<QuoteResult>(entity =>
        {
            entity.ToTable("QuoteResults");
            entity.HasKey(r => new { r.RequestId, r.Symbol });
            entity.Property(r => r.Symbol).HasMaxLength(SymbolValidator.MaxSymbolLength);
            entity.Property(r => r.Error).HasMaxLength(500);
        });

        modelBuilder.Entity<FyersSessionRecord>(entity =>
        {
            entity.ToTable("FyersSession");
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Id).ValueGeneratedNever();
            entity.Property(s => s.AppId).HasMaxLength(64);
        });

        modelBuilder.Entity<MutualFundNav>(entity =>
        {
            entity.ToTable("MutualFundNavs");
            entity.HasKey(n => n.SchemeCode);
            entity.Property(n => n.SchemeCode).ValueGeneratedNever();
            entity.Property(n => n.Isin).HasMaxLength(12);
            entity.Property(n => n.IsinReinvestment).HasMaxLength(12);
            entity.Property(n => n.SchemeName).HasMaxLength(500);
            entity.Property(n => n.FundHouse).HasMaxLength(200);
            entity.Property(n => n.Category).HasMaxLength(300);
            entity.Property(n => n.Plan).HasMaxLength(100);
            entity.Property(n => n.Option).HasMaxLength(200);
            entity.HasIndex(n => n.Isin);
            entity.HasIndex(n => n.IsinReinvestment);
        });

        // No foreign key to MutualFundNavs: every NAV refresh deletes and reinserts that table.
        modelBuilder.Entity<MyFundRecord>(entity =>
        {
            entity.ToTable("MyFunds");
            entity.HasKey(f => f.SchemeCode);
            entity.Property(f => f.SchemeCode).ValueGeneratedNever();
            entity.Property(f => f.SchemeName).HasMaxLength(500);
            entity.Property(f => f.FundHouse).HasMaxLength(200);
            entity.Property(f => f.Option).HasMaxLength(200);
            entity.Property(f => f.Category).HasMaxLength(300);
            entity.Property(f => f.ScanxScheme).HasMaxLength(300);
            entity.HasMany(f => f.Holdings)
                .WithOne()
                .HasForeignKey(h => h.SchemeCode)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FundHoldingRecord>(entity =>
        {
            entity.ToTable("FundHoldings");
            entity.HasKey(h => h.Id);
            entity.Property(h => h.AssetType).HasMaxLength(100);
            entity.Property(h => h.Name).HasMaxLength(300);
            entity.Property(h => h.Symbol).HasMaxLength(100);
            entity.Property(h => h.Isin).HasMaxLength(12);
            entity.Property(h => h.Sector).HasMaxLength(200);
            entity.Property(h => h.Percent).HasPrecision(9, 4);
            entity.Property(h => h.Shares).HasPrecision(20, 4);
            entity.HasIndex(h => new { h.SchemeCode, h.Month });
            entity.HasIndex(h => h.Isin);
        });

        modelBuilder.Entity<HoldingFetchRecord>(entity =>
        {
            entity.ToTable("HoldingFetches");
            entity.HasKey(f => f.Id);
            entity.Property(f => f.Error).HasMaxLength(1000);
            entity.HasIndex(f => f.CreatedAt);
            entity.HasMany(f => f.Funds)
                .WithOne()
                .HasForeignKey(f => f.FetchId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // No foreign key to MyFunds: a fund removed mid-fetch keeps its row here, marked as an error.
        modelBuilder.Entity<HoldingFetchFundRecord>(entity =>
        {
            entity.ToTable("HoldingFetchFunds");
            entity.HasKey(f => new { f.FetchId, f.SchemeCode });
            entity.Property(f => f.SchemeName).HasMaxLength(500);
            entity.Property(f => f.ScanxScheme).HasMaxLength(300);
            entity.Property(f => f.Error).HasMaxLength(500);
        });

        // No foreign key from FundHoldings: every symbol refresh deletes and reinserts this table.
        modelBuilder.Entity<FyersSymbol>(entity =>
        {
            entity.ToTable("FyersSymbols");
            entity.HasKey(s => s.Isin);
            entity.Property(s => s.Isin).HasMaxLength(12);
            entity.Property(s => s.Symbol).HasMaxLength(100);
            entity.Property(s => s.Name).HasMaxLength(300);
        });
    }
}
