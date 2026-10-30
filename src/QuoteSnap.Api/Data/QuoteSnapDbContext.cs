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

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<QuoteRequestStatus>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<QuoteResultStatus>().HaveConversion<string>().HaveMaxLength(32);
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
    }
}
