using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Hsm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:citext", ",,");

            migrationBuilder.CreateTable(
                name: "app_setting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true),
                    IsSecret = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_setting", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "app_setting_audit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ChangedBy = table.Column<string>(type: "text", nullable: true),
                    OldValue = table.Column<string>(type: "text", nullable: true),
                    NewValue = table.Column<string>(type: "text", nullable: true),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_setting_audit", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EntityId = table.Column<string>(type: "text", nullable: true),
                    EntityType = table.Column<string>(type: "text", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_documents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "email_batch",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: true),
                    FromEmail = table.Column<string>(type: "text", nullable: true),
                    FromName = table.Column<string>(type: "text", nullable: true),
                    data = table.Column<string>(type: "jsonb", nullable: false),
                    DocumentIds = table.Column<List<string>>(type: "text[]", nullable: true),
                    JobId = table.Column<string>(type: "text", nullable: true),
                    ProviderMessageId = table.Column<string>(type: "text", nullable: true),
                    OverallStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_batch", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "patient",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    Gender = table.Column<string>(type: "text", nullable: true),
                    birth_date = table.Column<string>(type: "text", nullable: true),
                    name = table.Column<string>(type: "jsonb", nullable: true),
                    telecom = table.Column<string>(type: "jsonb", nullable: true),
                    address = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_patient", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "proving_roots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_proving_roots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "templates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    schema = table.Column<string>(type: "jsonb", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    BaseTemplateId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_templates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_templates_templates_BaseTemplateId",
                        column: x => x.BaseTemplateId,
                        principalTable: "templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstName = table.Column<string>(type: "text", nullable: false),
                    SecondName = table.Column<string>(type: "text", nullable: true),
                    FirstLastName = table.Column<string>(type: "text", nullable: false),
                    SecondLastName = table.Column<string>(type: "text", nullable: true),
                    Gender = table.Column<string>(type: "text", nullable: true),
                    LastLoginAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OnboardingCompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UserName = table.Column<string>(type: "citext", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "citext", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "citext", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "citext", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "users_integration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "citext", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Functionality = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users_integration", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "document-audit-log",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document-audit-log", x => x.Id);
                    table.ForeignKey(
                        name: "FK_document-audit-log_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "document-link",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityId = table.Column<string>(type: "text", nullable: false),
                    EntityType = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document-link", x => x.Id);
                    table.ForeignKey(
                        name: "FK_document-link_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "documents-version",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Filename = table.Column<string>(type: "text", nullable: true),
                    MimeType = table.Column<string>(type: "text", nullable: true),
                    Size = table.Column<long>(type: "bigint", nullable: true),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_documents-version", x => x.Id);
                    table.ForeignKey(
                        name: "FK_documents-version_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "email_recipient",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToEmail = table.Column<string>(type: "text", nullable: false),
                    MessageId = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_recipient", x => x.Id);
                    table.ForeignKey(
                        name: "FK_email_recipient_email_batch_BatchId",
                        column: x => x.BatchId,
                        principalTable: "email_batch",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "patient_identifier",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    System = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false),
                    Use = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_patient_identifier", x => x.Id);
                    table.ForeignKey(
                        name: "FK_patient_identifier_patient_PatientId",
                        column: x => x.PatientId,
                        principalTable: "patient",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "proving_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    proving_root_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_proving_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_proving_items_proving_roots_proving_root_id",
                        column: x => x.proving_root_id,
                        principalTable: "proving_roots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "role_claims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_claims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_role_claims_roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "template_coms_email",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    FromEmail = table.Column<string>(type: "text", nullable: false),
                    FromName = table.Column<string>(type: "text", nullable: false),
                    Cc = table.Column<List<string>>(type: "text[]", nullable: true),
                    Bcc = table.Column<List<string>>(type: "text[]", nullable: true),
                    HasAttachment = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_template_coms_email", x => x.Id);
                    table.ForeignKey(
                        name: "FK_template_coms_email_templates_Id",
                        column: x => x.Id,
                        principalTable: "templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "template_coms_sms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    TemplateName = table.Column<string>(type: "text", nullable: false),
                    From = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_template_coms_sms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_template_coms_sms_templates_Id",
                        column: x => x.Id,
                        principalTable: "templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "template_docs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Format = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Size = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Orientation = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_template_docs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_template_docs_templates_Id",
                        column: x => x.Id,
                        principalTable: "templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "template_parse_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: true),
                    TemplateName = table.Column<string>(type: "text", nullable: false),
                    Category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    input = table.Column<string>(type: "jsonb", nullable: false),
                    OutputLength = table.Column<int>(type: "integer", nullable: true),
                    Success = table.Column<bool>(type: "boolean", nullable: false),
                    ErrorCode = table.Column<string>(type: "text", nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    TriggeredBy = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_template_parse_logs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_template_parse_logs_templates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "refresh_token_users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_token_users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_refresh_token_users_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_claims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_claims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_claims_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_logins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_logins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_user_logins_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_roles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_roles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_user_roles_roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_roles_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_tokens",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_tokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_user_tokens_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "refresh_token_user_integration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IntegrationAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_token_user_integration", x => x.Id);
                    table.ForeignKey(
                        name: "FK_refresh_token_user_integration_users_integration_Integratio~",
                        column: x => x.IntegrationAccountId,
                        principalTable: "users_integration",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "document-storage-object",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Path = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Bucket = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Region = table.Column<string>(type: "text", nullable: true),
                    ETag = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VersionId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document-storage-object", x => x.Id);
                    table.ForeignKey(
                        name: "FK_document-storage-object_documents-version_VersionId",
                        column: x => x.VersionId,
                        principalTable: "documents-version",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "documents-generated",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateName = table.Column<string>(type: "text", nullable: false),
                    data = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VersionId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_documents-generated", x => x.Id);
                    table.ForeignKey(
                        name: "FK_documents-generated_documents-version_VersionId",
                        column: x => x.VersionId,
                        principalTable: "documents-version",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "email_webhook_event",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    EventType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    rawPayload = table.Column<string>(type: "jsonb", nullable: false),
                    RecipientEmail = table.Column<string>(type: "text", nullable: false),
                    MessageId = table.Column<string>(type: "text", nullable: true),
                    RecipientId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_webhook_event", x => x.Id);
                    table.ForeignKey(
                        name: "FK_email_webhook_event_email_recipient_RecipientId",
                        column: x => x.RecipientId,
                        principalTable: "email_recipient",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "email_suppression",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SourceWebhookEventId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_suppression", x => x.Id);
                    table.ForeignKey(
                        name: "FK_email_suppression_email_webhook_event_SourceWebhookEventId",
                        column: x => x.SourceWebhookEventId,
                        principalTable: "email_webhook_event",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.InsertData(
                table: "roles",
                columns: new[] { "Id", "ConcurrencyStamp", "Name", "NormalizedName" },
                values: new object[,]
                {
                    { new Guid("02a76b6a-0a49-4737-9175-18200e696dea"), "02a76b6a-0a49-4737-9175-18200e696dea", "admission", "ADMISSION" },
                    { new Guid("04a7d82a-7c9d-1155-ac25-4f5f51fe70a0"), "04a7d82a-7c9d-1155-ac25-4f5f51fe70a0", "it", "IT" },
                    { new Guid("12d272e5-ebbb-e8d1-8d8e-ea0217919612"), "12d272e5-ebbb-e8d1-8d8e-ea0217919612", "clinical_researcher", "CLINICAL_RESEARCHER" },
                    { new Guid("13348d76-a955-2a4e-5a21-851b06398242"), "13348d76-a955-2a4e-5a21-851b06398242", "insurance_specialist", "INSURANCE_SPECIALIST" },
                    { new Guid("16511e78-e1a1-344a-eada-50159d589e69"), "16511e78-e1a1-344a-eada-50159d589e69", "nurse", "NURSE" },
                    { new Guid("272d36f0-554e-7795-cf46-1285e54f8650"), "272d36f0-554e-7795-cf46-1285e54f8650", "legal_counsel", "LEGAL_COUNSEL" },
                    { new Guid("28aa9971-2304-1f3d-0b9f-8a2c7ff18ec2"), "28aa9971-2304-1f3d-0b9f-8a2c7ff18ec2", "case_manager", "CASE_MANAGER" },
                    { new Guid("323d9907-6536-c3d0-1b9c-255d3530ff68"), "323d9907-6536-c3d0-1b9c-255d3530ff68", "quality_officer", "QUALITY_OFFICER" },
                    { new Guid("3637499d-6472-3e8e-a0c7-692eebf67f54"), "3637499d-6472-3e8e-a0c7-692eebf67f54", "crm_specialist", "CRM_SPECIALIST" },
                    { new Guid("3ae58ff7-103f-847b-4ec9-b58963a6d6cd"), "3ae58ff7-103f-847b-4ec9-b58963a6d6cd", "therapist", "THERAPIST" },
                    { new Guid("3bc68985-4309-2ba6-fda9-b35dccc71a30"), "3bc68985-4309-2ba6-fda9-b35dccc71a30", "maintenance", "MAINTENANCE" },
                    { new Guid("47872314-ed33-62cf-404a-a8f08ad669d2"), "47872314-ed33-62cf-404a-a8f08ad669d2", "patient_services", "PATIENT_SERVICES" },
                    { new Guid("4b59596a-5fa4-34ef-6b39-87ac3ee2c5c3"), "4b59596a-5fa4-34ef-6b39-87ac3ee2c5c3", "compliance_officer", "COMPLIANCE_OFFICER" },
                    { new Guid("52cc92eb-66a7-00c5-e001-d02f1eaa7878"), "52cc92eb-66a7-00c5-e001-d02f1eaa7878", "integration", "INTEGRATION" },
                    { new Guid("741af960-5824-60ae-6572-f40dd7df0a12"), "741af960-5824-60ae-6572-f40dd7df0a12", "payroll", "PAYROLL" },
                    { new Guid("750dfa88-849f-475b-c044-c2cd44e29082"), "750dfa88-849f-475b-c044-c2cd44e29082", "developer", "DEVELOPER" },
                    { new Guid("7aff9522-d88b-f2b3-884c-6482146e3ded"), "7aff9522-d88b-f2b3-884c-6482146e3ded", "patient", "PATIENT" },
                    { new Guid("856e9b9c-304f-8b9d-7e71-c662284daf92"), "856e9b9c-304f-8b9d-7e71-c662284daf92", "financial_analyst", "FINANCIAL_ANALYST" },
                    { new Guid("89593dc5-9dc5-c2e3-5d71-6daf9f544c0f"), "89593dc5-9dc5-c2e3-5d71-6daf9f544c0f", "paralegal", "PARALEGAL" },
                    { new Guid("89bef472-ebd6-14ab-96e2-1e38bcd7c8ca"), "89bef472-ebd6-14ab-96e2-1e38bcd7c8ca", "doctor", "DOCTOR" },
                    { new Guid("91a2fd40-32f4-5b9e-9a5e-41bc372236f4"), "91a2fd40-32f4-5b9e-9a5e-41bc372236f4", "research_coordinator", "RESEARCH_COORDINATOR" },
                    { new Guid("96be82bd-3c4f-027c-926b-49ba8bd13a5e"), "96be82bd-3c4f-027c-926b-49ba8bd13a5e", "patient_advocate", "PATIENT_ADVOCATE" },
                    { new Guid("9a564ad3-aab7-4da5-acd7-15ae64953455"), "9a564ad3-aab7-4da5-acd7-15ae64953455", "family", "FAMILY" },
                    { new Guid("9c9dba29-5aef-4666-1116-a24938bb9307"), "9c9dba29-5aef-4666-1116-a24938bb9307", "technician", "TECHNICIAN" },
                    { new Guid("9ec9d829-5bc2-1027-07f0-5eace87ec009"), "9ec9d829-5bc2-1027-07f0-5eace87ec009", "accountant", "ACCOUNTANT" },
                    { new Guid("a5ff8f63-f338-9108-158f-5ffdf658cde4"), "a5ff8f63-f338-9108-158f-5ffdf658cde4", "social_worker", "SOCIAL_WORKER" },
                    { new Guid("a6b419a9-d9cd-a925-7e96-9ae31b2c092b"), "a6b419a9-d9cd-a925-7e96-9ae31b2c092b", "scheduling", "SCHEDULING" },
                    { new Guid("a8f3f682-2809-3cc7-7b78-36dc63ffd8d6"), "a8f3f682-2809-3cc7-7b78-36dc63ffd8d6", "guest_relations", "GUEST_RELATIONS" },
                    { new Guid("a9524f1e-f059-4605-bff6-e2f2adf6bbea"), "a9524f1e-f059-4605-bff6-e2f2adf6bbea", "designer", "DESIGNER" },
                    { new Guid("ca26992e-a3f5-364b-25f9-f73567680a47"), "ca26992e-a3f5-364b-25f9-f73567680a47", "pharmacist", "PHARMACIST" },
                    { new Guid("d3a0d038-5f64-91da-31b7-04c5390de794"), "d3a0d038-5f64-91da-31b7-04c5390de794", "data_analyst", "DATA_ANALYST" },
                    { new Guid("db5968ba-517f-9dd9-278b-e471b4ecbcf1"), "db5968ba-517f-9dd9-278b-e471b4ecbcf1", "process_analyst", "PROCESS_ANALYST" },
                    { new Guid("e32ca6c5-7ffa-866d-af00-09389ccd8152"), "e32ca6c5-7ffa-866d-af00-09389ccd8152", "auditor", "AUDITOR" },
                    { new Guid("e478c788-c4d1-6686-923e-11677d00849c"), "e478c788-c4d1-6686-923e-11677d00849c", "community_manager", "COMMUNITY_MANAGER" },
                    { new Guid("e576698c-41b5-1504-bde9-08bd4dee15df"), "e576698c-41b5-1504-bde9-08bd4dee15df", "admin", "ADMIN" },
                    { new Guid("e6477b63-38ae-42b2-015f-de4b9f03a693"), "e6477b63-38ae-42b2-015f-de4b9f03a693", "human_resources", "HUMAN_RESOURCES" },
                    { new Guid("eb3c2d5d-be7a-2355-4427-6d47d36a8175"), "eb3c2d5d-be7a-2355-4427-6d47d36a8175", "security", "SECURITY" },
                    { new Guid("ecc7950c-cee1-971a-5027-5ef1c6d7ad6b"), "ecc7950c-cee1-971a-5027-5ef1c6d7ad6b", "billing", "BILLING" },
                    { new Guid("f97168c2-6492-488a-0184-8b93f9ccb46f"), "f97168c2-6492-488a-0184-8b93f9ccb46f", "housekeeping", "HOUSEKEEPING" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_app_setting_Key",
                table: "app_setting",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_app_setting_audit_Key",
                table: "app_setting_audit",
                column: "Key");

            migrationBuilder.CreateIndex(
                name: "IX_document-audit-log_DocumentId",
                table: "document-audit-log",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_document-link_DocumentId",
                table: "document-link",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_document-link_EntityId_EntityType",
                table: "document-link",
                columns: new[] { "EntityId", "EntityType" });

            migrationBuilder.CreateIndex(
                name: "IX_document-storage-object_VersionId",
                table: "document-storage-object",
                column: "VersionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_documents_CreatedBy_CreatedAt",
                table: "documents",
                columns: new[] { "CreatedBy", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_documents-generated_VersionId",
                table: "documents-generated",
                column: "VersionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_documents-version_DocumentId_Version",
                table: "documents-version",
                columns: new[] { "DocumentId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_email_recipient_BatchId",
                table: "email_recipient",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_email_recipient_ToEmail",
                table: "email_recipient",
                column: "ToEmail");

            migrationBuilder.CreateIndex(
                name: "IX_email_suppression_Email",
                table: "email_suppression",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_email_suppression_SourceWebhookEventId",
                table: "email_suppression",
                column: "SourceWebhookEventId");

            migrationBuilder.CreateIndex(
                name: "IX_email_webhook_event_MessageId",
                table: "email_webhook_event",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_email_webhook_event_RecipientEmail",
                table: "email_webhook_event",
                column: "RecipientEmail");

            migrationBuilder.CreateIndex(
                name: "IX_email_webhook_event_RecipientId",
                table: "email_webhook_event",
                column: "RecipientId");

            migrationBuilder.CreateIndex(
                name: "IX_patient_identifier_PatientId",
                table: "patient_identifier",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "uq_patient_identifier_system_value",
                table: "patient_identifier",
                columns: new[] { "System", "Value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_proving_items_proving_root_id",
                table: "proving_items",
                column: "proving_root_id");

            migrationBuilder.CreateIndex(
                name: "IX_refresh_token_user_integration_IntegrationAccountId_IsActive",
                table: "refresh_token_user_integration",
                columns: new[] { "IntegrationAccountId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_refresh_token_user_integration_TokenHash",
                table: "refresh_token_user_integration",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_refresh_token_users_TokenHash",
                table: "refresh_token_users",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_refresh_token_users_UserId_IsActive",
                table: "refresh_token_users",
                columns: new[] { "UserId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_role_claims_RoleId",
                table: "role_claims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "roles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_template_parse_logs_CreatedAt",
                table: "template_parse_logs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_template_parse_logs_TemplateId_CreatedAt",
                table: "template_parse_logs",
                columns: new[] { "TemplateId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_templates_BaseTemplateId",
                table: "templates",
                column: "BaseTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_templates_Name",
                table: "templates",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_claims_UserId",
                table: "user_claims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_user_logins_UserId",
                table: "user_logins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_user_roles_RoleId",
                table: "user_roles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "ix_users_normalized_email",
                table: "users",
                column: "NormalizedEmail",
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_users_normalized_user_name",
                table: "users",
                column: "NormalizedUserName",
                unique: true,
                filter: "\"DeletedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "app_setting");

            migrationBuilder.DropTable(
                name: "app_setting_audit");

            migrationBuilder.DropTable(
                name: "document-audit-log");

            migrationBuilder.DropTable(
                name: "document-link");

            migrationBuilder.DropTable(
                name: "document-storage-object");

            migrationBuilder.DropTable(
                name: "documents-generated");

            migrationBuilder.DropTable(
                name: "email_suppression");

            migrationBuilder.DropTable(
                name: "patient_identifier");

            migrationBuilder.DropTable(
                name: "proving_items");

            migrationBuilder.DropTable(
                name: "refresh_token_user_integration");

            migrationBuilder.DropTable(
                name: "refresh_token_users");

            migrationBuilder.DropTable(
                name: "role_claims");

            migrationBuilder.DropTable(
                name: "template_coms_email");

            migrationBuilder.DropTable(
                name: "template_coms_sms");

            migrationBuilder.DropTable(
                name: "template_docs");

            migrationBuilder.DropTable(
                name: "template_parse_logs");

            migrationBuilder.DropTable(
                name: "user_claims");

            migrationBuilder.DropTable(
                name: "user_logins");

            migrationBuilder.DropTable(
                name: "user_roles");

            migrationBuilder.DropTable(
                name: "user_tokens");

            migrationBuilder.DropTable(
                name: "documents-version");

            migrationBuilder.DropTable(
                name: "email_webhook_event");

            migrationBuilder.DropTable(
                name: "patient");

            migrationBuilder.DropTable(
                name: "proving_roots");

            migrationBuilder.DropTable(
                name: "users_integration");

            migrationBuilder.DropTable(
                name: "templates");

            migrationBuilder.DropTable(
                name: "roles");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "documents");

            migrationBuilder.DropTable(
                name: "email_recipient");

            migrationBuilder.DropTable(
                name: "email_batch");
        }
    }
}
