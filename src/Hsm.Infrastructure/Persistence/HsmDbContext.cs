using Hsm.Domain.Coms;
using Hsm.Domain.Identity;
using Hsm.Domain.Proving;
using Hsm.Domain.Settings;
using Hsm.Domain.Templates;
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

    public DbSet<Template> Templates => Set<Template>();
    public DbSet<TemplateParseLog> TemplateParseLogs => Set<TemplateParseLog>();

    public DbSet<EmailBatch> EmailBatches => Set<EmailBatch>();
    public DbSet<EmailRecipient> EmailRecipients => Set<EmailRecipient>();
    public DbSet<EmailSuppression> EmailSuppressions => Set<EmailSuppression>();
    public DbSet<EmailWebhookEvent> EmailWebhookEvents => Set<EmailWebhookEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureIdentity(modelBuilder);
        ConfigureSettings(modelBuilder);
        ConfigureTemplates(modelBuilder);
        ConfigureComs(modelBuilder);
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
    /// Template tables mirror the frozen schema (templates.* in Postgres):
    /// one parent row plus at most one shape row (email/sms/doc) sharing the
    /// parent's key, a nullable self-reference to the BASE template that
    /// RESTRICTS delete (the domain 409 fires first; the constraint is the
    /// backstop), and a parse log whose FK nulls on template deletion so the
    /// audit trail survives. Frozen enum columns are plain strings here — the
    /// catalogs are the source of truth, observably identical via the API.
    /// </summary>
    private static void ConfigureTemplates(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Template>(template =>
        {
            template.ToTable("templates");
            template.HasKey(t => t.Id);
            template.Property(t => t.Category).HasMaxLength(50);
            template.Property(t => t.Name).HasMaxLength(200);
            template.Property(t => t.SchemaJson).HasColumnName("schema").HasColumnType("jsonb");
            template.HasIndex(t => t.Name).IsUnique();
            template.HasOne(t => t.BaseTemplate)
                .WithMany()
                .HasForeignKey(t => t.BaseTemplateId)
                .OnDelete(DeleteBehavior.Restrict);
            template.HasOne(t => t.Email)
                .WithOne()
                .HasForeignKey<TemplateEmail>(e => e.Id)
                .OnDelete(DeleteBehavior.Cascade);
            template.HasOne(t => t.Sms)
                .WithOne()
                .HasForeignKey<TemplateSms>(s => s.Id)
                .OnDelete(DeleteBehavior.Cascade);
            template.HasOne(t => t.Doc)
                .WithOne()
                .HasForeignKey<TemplateDoc>(d => d.Id)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TemplateEmail>(email =>
        {
            email.ToTable("template_coms_email");
            email.HasKey(e => e.Id);
            email.Property(e => e.Subject).HasMaxLength(500);
        });

        modelBuilder.Entity<TemplateSms>(sms =>
        {
            sms.ToTable("template_coms_sms");
            sms.HasKey(s => s.Id);
        });

        modelBuilder.Entity<TemplateDoc>(doc =>
        {
            doc.ToTable("template_docs");
            doc.HasKey(d => d.Id);
            doc.Property(d => d.DocumentCode).HasMaxLength(50);
            doc.Property(d => d.Format).HasMaxLength(20);
            doc.Property(d => d.Size).HasMaxLength(20);
            doc.Property(d => d.Orientation).HasMaxLength(20);
        });

        modelBuilder.Entity<TemplateParseLog>(log =>
        {
            log.ToTable("template_parse_logs");
            log.HasKey(l => l.Id);
            log.Property(l => l.Category).HasMaxLength(50);
            log.Property(l => l.InputJson).HasColumnName("input").HasColumnType("jsonb");
            log.HasIndex(l => new { l.TemplateId, l.CreatedAt });
            log.HasIndex(l => l.CreatedAt);
            log.HasOne<Template>()
                .WithMany()
                .HasForeignKey(l => l.TemplateId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    /// <summary>
    /// Communications tables mirror the frozen coms.* schema. The
    /// batch→recipient relationship is the aggregate the stack was chosen
    /// for: required FK plus cascade, so a recipient removed from the batch's
    /// collection is DELETED (orphan removal), proven by integration test.
    /// </summary>
    private static void ConfigureComs(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EmailBatch>(batch =>
        {
            batch.ToTable("email_batch");
            batch.HasKey(b => b.Id);
            batch.Property(b => b.DataJson).HasColumnName("data").HasColumnType("jsonb");
            batch.Property(b => b.OverallStatus).HasMaxLength(20);
            batch.HasMany(b => b.Recipients)
                .WithOne()
                .HasForeignKey(r => r.BatchId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EmailRecipient>(recipient =>
        {
            recipient.ToTable("email_recipient");
            recipient.HasKey(r => r.Id);
            recipient.Property(r => r.Status).HasMaxLength(20);
            recipient.HasIndex(r => r.ToEmail);
        });

        modelBuilder.Entity<EmailSuppression>(suppression =>
        {
            suppression.ToTable("email_suppression");
            suppression.HasKey(s => s.Id);
            suppression.Property(s => s.Reason).HasMaxLength(30);
            suppression.HasIndex(s => s.Email).IsUnique();
            suppression.HasOne<EmailWebhookEvent>()
                .WithMany()
                .HasForeignKey(s => s.SourceWebhookEventId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<EmailWebhookEvent>(webhookEvent =>
        {
            webhookEvent.ToTable("email_webhook_event");
            webhookEvent.HasKey(e => e.Id);
            webhookEvent.Property(e => e.EventType).HasMaxLength(30);
            webhookEvent.Property(e => e.RawPayloadJson).HasColumnName("rawPayload").HasColumnType("jsonb");
            webhookEvent.HasIndex(e => e.RecipientEmail);
            webhookEvent.HasIndex(e => e.MessageId);
            webhookEvent.HasOne<EmailRecipient>()
                .WithMany()
                .HasForeignKey(e => e.RecipientId)
                .OnDelete(DeleteBehavior.SetNull);
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
