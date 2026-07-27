using Hsm.Domain.Proving;
using Microsoft.EntityFrameworkCore;

namespace Hsm.Infrastructure.Persistence;

public class HsmDbContext(DbContextOptions<HsmDbContext> options) : DbContext(options)
{
    public DbSet<ProvingRoot> ProvingRoots => Set<ProvingRoot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProvingRoot>(root =>
        {
            root.ToTable("proving_roots");
            root.HasKey(r => r.Id);
            root.Property(r => r.Name).HasMaxLength(200);

            // Children are reachable only through the aggregate; removing one
            // from the collection deletes the row (orphan removal via the
            // required relationship), which is the change-tracking behavior
            // the stack was chosen for.
            root.HasMany(r => r.Items)
                .WithOne()
                .HasForeignKey("proving_root_id")
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);

            root.Navigation(r => r.Items)
                .UsePropertyAccessMode(PropertyAccessMode.Field)
                .HasField("_items");
        });

        modelBuilder.Entity<ProvingItem>(item =>
        {
            item.ToTable("proving_items");
            item.HasKey(i => i.Id);
            item.Property(i => i.Label).HasMaxLength(200);
        });
    }
}
