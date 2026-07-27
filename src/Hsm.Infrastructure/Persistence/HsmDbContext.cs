using Hsm.Domain.Identity;
using Hsm.Domain.Proving;
using Hsm.Domain.Settings;
using Microsoft.EntityFrameworkCore;

namespace Hsm.Infrastructure.Persistence;

public class HsmDbContext(DbContextOptions<HsmDbContext> options) : DbContext(options)
{
    public DbSet<ProvingRoot> ProvingRoots => Set<ProvingRoot>();

    public DbSet<User> Users => Set<User>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<IntegrationAccount> IntegrationAccounts => Set<IntegrationAccount>();
    public DbSet<UserRefreshToken> UserRefreshTokens => Set<UserRefreshToken>();
    public DbSet<IntegrationRefreshToken> IntegrationRefreshTokens => Set<IntegrationRefreshToken>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<AppSettingAudit> AppSettingAudits => Set<AppSettingAudit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureIdentity(modelBuilder);
        ConfigureSettings(modelBuilder);
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

    /// <summary>
    /// Identity tables mirror the frozen schema's semantics: case-insensitive
    /// unique username/email (citext), one-active refresh-token rows in TWO
    /// separate stores (users vs integrations), and hashed single-use
    /// password-reset tokens.
    /// </summary>
    private static void ConfigureIdentity(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("citext");

        modelBuilder.Entity<User>(user =>
        {
            user.ToTable("users");
            user.HasKey(u => u.Id);
            user.Property(u => u.Username).HasColumnType("citext");
            user.Property(u => u.Email).HasColumnType("citext");
            user.HasIndex(u => u.Username).IsUnique().HasFilter("\"DeletedAt\" IS NULL");
            user.HasIndex(u => u.Email).IsUnique().HasFilter("\"DeletedAt\" IS NULL");
            user.HasMany(u => u.Roles)
                .WithOne()
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserRole>(role =>
        {
            role.ToTable("user_roles");
            role.HasKey(r => r.Id);
            role.HasIndex(r => new { r.UserId, r.Domain, r.Role }).IsUnique();
        });

        modelBuilder.Entity<IntegrationAccount>(account =>
        {
            account.ToTable("users_integration");
            account.HasKey(a => a.Id);
            account.Property(a => a.Name).HasColumnType("citext");
        });

        modelBuilder.Entity<UserRefreshToken>(token =>
        {
            token.ToTable("refresh_token_users");
            token.HasKey(t => t.Id);
            token.HasIndex(t => t.TokenHash).IsUnique();
            token.HasIndex(t => new { t.UserId, t.IsActive });
            token.HasOne<User>()
                .WithMany()
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<IntegrationRefreshToken>(token =>
        {
            token.ToTable("refresh_token_user_integration");
            token.HasKey(t => t.Id);
            token.HasIndex(t => t.TokenHash).IsUnique();
            token.HasIndex(t => new { t.IntegrationAccountId, t.IsActive });
            token.HasOne<IntegrationAccount>()
                .WithMany()
                .HasForeignKey(t => t.IntegrationAccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PasswordResetToken>(token =>
        {
            token.ToTable("password_reset_tokens");
            token.HasKey(t => t.Id);
            token.HasIndex(t => t.TokenHash);
            token.HasOne<User>()
                .WithMany()
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    /// <summary>
    /// Settings tables mirror the frozen schema's semantics: one row per key
    /// (unique), and an append-only audit table whose rows never store secret
    /// plaintext. The frozen category column was a Postgres enum; a plain
    /// string is used here — observably identical through the API, and the
    /// catalog (not the column type) is the source of truth for valid values.
    /// </summary>
    private static void ConfigureSettings(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppSetting>(setting =>
        {
            setting.ToTable("app_setting");
            setting.HasKey(s => s.Id);
            setting.Property(s => s.Key).HasMaxLength(200);
            setting.Property(s => s.Category).HasMaxLength(50);
            setting.HasIndex(s => s.Key).IsUnique();
        });

        modelBuilder.Entity<AppSettingAudit>(audit =>
        {
            audit.ToTable("app_setting_audit");
            audit.HasKey(a => a.Id);
            audit.Property(a => a.Key).HasMaxLength(200);
            audit.Property(a => a.Category).HasMaxLength(50);
            audit.HasIndex(a => a.Key);
        });
    }
}
