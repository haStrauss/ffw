using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ffw.Data;

public class AppDbContext : IdentityDbContext<IdentityUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<FotoKategorie> Kategorien => Set<FotoKategorie>();

    public DbSet<Foto> Fotos => Set<Foto>();

    public DbSet<DokumentKategorie> DokumentKategorien => Set<DokumentKategorie>();

    public DbSet<Dokument> Dokumente => Set<Dokument>();

    public DbSet<LoeschspielErgebnis> LoeschspielErgebnisse => Set<LoeschspielErgebnis>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<FotoKategorie>(e =>
        {
            e.Property(k => k.Name).IsRequired().HasMaxLength(100);
            e.HasIndex(k => k.Name).IsUnique();
        });

        builder.Entity<Foto>(e =>
        {
            e.HasIndex(f => f.DateiId).IsUnique();
            e.HasOne(f => f.Kategorie)
             .WithMany(k => k.Fotos)
             .HasForeignKey(f => f.KategorieId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<DokumentKategorie>(e =>
        {
            e.Property(k => k.Name).IsRequired().HasMaxLength(100);
            e.HasIndex(k => k.Name).IsUnique();
        });

        builder.Entity<Dokument>(e =>
        {
            e.Property(d => d.Name).IsRequired().HasMaxLength(255);
            e.HasIndex(d => d.DateiId).IsUnique();
            e.HasOne(d => d.Kategorie)
             .WithMany(k => k.Dokumente)
             .HasForeignKey(d => d.KategorieId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<LoeschspielErgebnis>(e =>
        {
            e.Property(x => x.Name).IsRequired().HasMaxLength(20);
            e.HasIndex(x => x.SpielId).IsUnique();
            e.HasIndex(x => x.Punkte);
        });
    }
}
