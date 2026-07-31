using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

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
                    Username = table.Column<string>(type: "citext", nullable: false),
                    Email = table.Column<string>(type: "citext", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    FirstName = table.Column<string>(type: "text", nullable: false),
                    SecondName = table.Column<string>(type: "text", nullable: true),
                    FirstLastName = table.Column<string>(type: "text", nullable: false),
                    SecondLastName = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    Gender = table.Column<string>(type: "text", nullable: true),
                    LastLoginAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OnboardingCompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    EmailVerified = table.Column<bool>(type: "boolean", nullable: false),
                    PhoneVerified = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
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
                name: "password_reset_tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_password_reset_tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_password_reset_tokens_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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
                name: "user_roles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Domain = table.Column<string>(type: "text", nullable: false),
                    Role = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_roles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_roles_users_UserId",
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
                name: "IX_password_reset_tokens_TokenHash",
                table: "password_reset_tokens",
                column: "TokenHash");

            migrationBuilder.CreateIndex(
                name: "IX_password_reset_tokens_UserId",
                table: "password_reset_tokens",
                column: "UserId");

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
                name: "IX_user_roles_UserId_Domain_Role",
                table: "user_roles",
                columns: new[] { "UserId", "Domain", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_Email",
                table: "users",
                column: "Email",
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_users_Username",
                table: "users",
                column: "Username",
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
                name: "password_reset_tokens");

            migrationBuilder.DropTable(
                name: "patient_identifier");

            migrationBuilder.DropTable(
                name: "proving_items");

            migrationBuilder.DropTable(
                name: "refresh_token_user_integration");

            migrationBuilder.DropTable(
                name: "refresh_token_users");

            migrationBuilder.DropTable(
                name: "template_coms_email");

            migrationBuilder.DropTable(
                name: "template_coms_sms");

            migrationBuilder.DropTable(
                name: "template_docs");

            migrationBuilder.DropTable(
                name: "template_parse_logs");

            migrationBuilder.DropTable(
                name: "user_roles");

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
