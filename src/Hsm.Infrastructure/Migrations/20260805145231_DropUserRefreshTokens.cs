using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hsm.Infrastructure.Migrations
{
    /// <summary>
    /// Drops <c>refresh_token_users</c>. Nothing writes it any more: registering,
    /// signing in and completing onboarding all return the user row and the door
    /// writes an Identity session cookie instead of a JWT pair, so a human is
    /// never issued a refresh token to store the hash of. Session revocation
    /// moved to the security stamp, which covers every session rather than only
    /// the ones that had a token. The integration table
    /// (<c>refresh_token_user_integration</c>) is untouched — that is the one
    /// caller that still holds a refresh token, until Task 14.
    /// </summary>
    public partial class DropUserRefreshTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "refresh_token_users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "refresh_token_users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    TokenHash = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "IX_refresh_token_users_TokenHash",
                table: "refresh_token_users",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_refresh_token_users_UserId_IsActive",
                table: "refresh_token_users",
                columns: new[] { "UserId", "IsActive" });
        }
    }
}
