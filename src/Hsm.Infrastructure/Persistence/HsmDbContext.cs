using Hsm.Domain.Clinical;
using Hsm.Domain.Coms;
using Hsm.Domain.Docs;
using Hsm.Domain.Identity;
using Hsm.Domain.Proving;
using Hsm.Domain.Settings;
using Hsm.Domain.Templates;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Hsm.Infrastructure.Persistence;

/// <summary>
/// One context for the whole schema, and an <see cref="IdentityDbContext{TUser, TRole, TKey}"/>
/// so ASP.NET Core Identity's stores write through the SAME DbContext — and
/// therefore the same connection and the same ambient transaction — as every
/// other adapter in the scope.
/// </summary>
public class HsmDbContext(DbContextOptions<HsmDbContext> options)
    : IdentityDbContext<HsmUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<ProvingRoot> ProvingRoots => Set<ProvingRoot>();

    // Users, Roles, UserRoles, UserClaims, UserLogins, UserTokens and
    // RoleClaims all come from IdentityDbContext; only the non-Identity
    // membership tables are declared here.
    public DbSet<IntegrationAccount> IntegrationAccounts => Set<IntegrationAccount>();
    public DbSet<IntegrationRefreshToken> IntegrationRefreshTokens => Set<IntegrationRefreshToken>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();

    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<AppSettingAudit> AppSettingAudits => Set<AppSettingAudit>();

    public DbSet<Template> Templates => Set<Template>();
    public DbSet<TemplateParseLog> TemplateParseLogs => Set<TemplateParseLog>();

    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();
    public DbSet<DocumentStorageObject> DocumentStorageObjects => Set<DocumentStorageObject>();
    public DbSet<DocumentLink> DocumentLinks => Set<DocumentLink>();
    public DbSet<DocumentGenerated> DocumentGeneratedRecords => Set<DocumentGenerated>();
    public DbSet<DocumentAuditLog> DocumentAuditLogs => Set<DocumentAuditLog>();

    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<PatientIdentifier> PatientIdentifiers => Set<PatientIdentifier>();

    public DbSet<EmailBatch> EmailBatches => Set<EmailBatch>();
    public DbSet<EmailRecipient> EmailRecipients => Set<EmailRecipient>();
    public DbSet<EmailSuppression> EmailSuppressions => Set<EmailSuppression>();
    public DbSet<EmailWebhookEvent> EmailWebhookEvents => Set<EmailWebhookEvent>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // FIRST: Identity's own configuration has to be in place before any of
        // it is overridden below.
        base.OnModelCreating(builder);
        var modelBuilder = builder;
        ConfigureIdentity(modelBuilder);
        ConfigureClinical(modelBuilder);
        ConfigureSettings(modelBuilder);
        ConfigureTemplates(modelBuilder);
        ConfigureComs(modelBuilder);
        ConfigureDocs(modelBuilder);
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
    /// Identity's tables, mapped onto this repository's snake_case names, with
    /// the two properties the hospital schema adds to the default: citext
    /// login/email columns and unique indexes filtered on DeletedAt, so a
    /// soft-deleted account frees its username and email for reuse.
    ///
    /// <para>The refresh-token tables are NOT Identity types. Integration
    /// accounts cannot hold a browser cookie and keep JWTs (Task 14); the user
    /// refresh-token table survives only until Task 12 replaces browser
    /// sessions with the Identity cookie, which is what retires it.</para>
    /// </summary>
    private static void ConfigureIdentity(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("citext");

        modelBuilder.Entity<HsmUser>(user =>
        {
            user.ToTable("users");
            user.Property(u => u.UserName).HasColumnType("citext").HasMaxLength(256);
            user.Property(u => u.NormalizedUserName).HasColumnType("citext").HasMaxLength(256);
            user.Property(u => u.Email).HasColumnType("citext").HasMaxLength(256);
            user.Property(u => u.NormalizedEmail).HasColumnType("citext").HasMaxLength(256);

            // Identity declares these two indexes unique and unfiltered; the
            // filter is what makes soft delete free the name.
            user.HasIndex(u => u.NormalizedUserName)
                .IsUnique()
                .HasFilter("\"DeletedAt\" IS NULL")
                .HasDatabaseName("ix_users_normalized_user_name");
            user.HasIndex(u => u.NormalizedEmail)
                .IsUnique()
                .HasFilter("\"DeletedAt\" IS NULL")
                .HasDatabaseName("ix_users_normalized_email");
        });

        modelBuilder.Entity<IdentityRole<Guid>>(role =>
        {
            role.ToTable("roles");
            role.HasData(RoleCatalog.All.Select(name => new IdentityRole<Guid>
            {
                Id = RoleCatalog.IdFor(name),
                Name = name,
                NormalizedName = name.ToUpperInvariant(),
                // A fixed stamp, because a random one per model build would make
                // every `migrations add` produce a spurious update.
                ConcurrencyStamp = RoleCatalog.IdFor(name).ToString(),
            }));
        });

        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");

        // Integration accounts and their refresh tokens are NOT Identity types;
        // they keep their own tables (see IdentityStores).
        modelBuilder.Entity<IntegrationAccount>(account =>
        {
            account.ToTable("users_integration");
            account.HasKey(a => a.Id);
            account.Property(a => a.Name).HasColumnType("citext");
        });

        modelBuilder.Entity<UserSession>(session =>
        {
            session.ToTable("user_sessions");
            session.HasKey(s => s.Id);

            // The validation path looks a session up BY ID, so the primary key
            // is the whole index it needs. The second index serves the other two
            // accesses, both of which are per user: reclaiming expired rows at
            // sign-in, and cascading when an account is deleted.
            session.HasIndex(s => new { s.UserId, s.ExpiresAt });
            session.HasOne<HsmUser>()
                .WithMany()
                .HasForeignKey(s => s.UserId)
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
    }

    /// <summary>
    /// Clinical patient tables: searchable scalars as real columns, complex
    /// FHIR datatypes as jsonb, and the normalized identifier child table
    /// with the unique (system, value) index that backs the handler's 409
    /// duplicate-identifier response.
    /// </summary>
    private static void ConfigureClinical(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Patient>(patient =>
        {
            patient.ToTable("patient");
            patient.HasKey(p => p.Id);
            patient.Property(p => p.BirthDate).HasColumnName("birth_date");
            patient.Property(p => p.NameJson).HasColumnName("name").HasColumnType("jsonb");
            patient.Property(p => p.TelecomJson).HasColumnName("telecom").HasColumnType("jsonb");
            patient.Property(p => p.AddressJson).HasColumnName("address").HasColumnType("jsonb");
            patient.HasMany(p => p.Identifiers)
                .WithOne()
                .HasForeignKey(i => i.PatientId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
            // Identifier rows always travel with the patient, so the
            // relation is eager-loaded.
            patient.Navigation(p => p.Identifiers).AutoInclude();
        });

        modelBuilder.Entity<PatientIdentifier>(identifier =>
        {
            identifier.ToTable("patient_identifier");
            identifier.HasKey(i => i.Id);
            identifier.HasIndex(i => new { i.System, i.Value })
                .IsUnique()
                .HasDatabaseName("uq_patient_identifier_system_value");
        });
    }

    /// <summary>
    /// Template tables (templates.* in Postgres): one parent row plus at
    /// most one shape row (email/sms/doc) sharing the parent's key, a
    /// nullable self-reference to the BASE template that RESTRICTS delete
    /// (the domain 409 fires first; the constraint is the backstop), and a
    /// parse log whose FK nulls on template deletion so the audit trail
    /// survives. Enum columns are plain strings here — the catalogs are the
    /// source of truth, observably identical via the API.
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
    /// Communications tables. The batch→recipient relationship is the
    /// aggregate the stack was chosen for: required FK plus cascade, so a
    /// recipient removed from the batch's collection is DELETED (orphan
    /// removal), proven by integration test.
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
    /// Document tables (note the hyphenated table names): metadata rows
    /// only — the binary itself lives in the blob store, and a
    /// schema-inspection test proves no binary-capable column exists on any
    /// of these tables. Versions are relational (one blob key per version
    /// row); links and audits hang off the document and cascade with the
    /// ROW's hard delete only — the API soft-deletes, so in practice they
    /// survive.
    /// </summary>
    private static void ConfigureDocs(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Document>(document =>
        {
            document.ToTable("documents");
            document.HasKey(d => d.Id);
            document.Property(d => d.Title).HasMaxLength(500);
            document.Property(d => d.Type).HasMaxLength(20);
            document.Property(d => d.Status).HasMaxLength(20);
            document.Property(d => d.Source).HasMaxLength(20);
            document.HasIndex(d => new { d.CreatedBy, d.CreatedAt });
            document.HasMany(d => d.Versions)
                .WithOne()
                .HasForeignKey(v => v.DocumentId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
            document.HasMany(d => d.Links)
                .WithOne()
                .HasForeignKey(l => l.DocumentId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
            document.HasMany(d => d.Audits)
                .WithOne()
                .HasForeignKey(a => a.DocumentId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DocumentVersion>(version =>
        {
            version.ToTable("documents-version");
            version.HasKey(v => v.Id);
            version.HasIndex(v => new { v.DocumentId, v.Version }).IsUnique();
            version.HasOne(v => v.Storage)
                .WithOne()
                .HasForeignKey<DocumentStorageObject>(s => s.VersionId)
                .OnDelete(DeleteBehavior.Cascade);
            version.HasOne(v => v.Generated)
                .WithOne()
                .HasForeignKey<DocumentGenerated>(g => g.VersionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DocumentStorageObject>(storage =>
        {
            storage.ToTable("document-storage-object");
            storage.HasKey(s => s.Id);
            storage.Property(s => s.Path).HasMaxLength(1024);
            storage.Property(s => s.Bucket).HasMaxLength(200);
        });

        modelBuilder.Entity<DocumentLink>(link =>
        {
            link.ToTable("document-link");
            link.HasKey(l => l.Id);
            link.HasIndex(l => new { l.EntityId, l.EntityType });
        });

        modelBuilder.Entity<DocumentGenerated>(generated =>
        {
            generated.ToTable("documents-generated");
            generated.HasKey(g => g.Id);
            generated.Property(g => g.DataJson).HasColumnName("data").HasColumnType("jsonb");
        });

        modelBuilder.Entity<DocumentAuditLog>(audit =>
        {
            audit.ToTable("document-audit-log");
            audit.HasKey(a => a.Id);
            audit.Property(a => a.Action).HasMaxLength(100);
        });
    }

    /// <summary>
    /// Settings tables: one row per key (unique), and an append-only audit
    /// table whose rows never store secret plaintext. The category column is
    /// a plain string rather than a Postgres enum — the catalog (not the
    /// column type) is the source of truth for valid values.
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
