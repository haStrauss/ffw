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
    }
}
