using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hsm.Infrastructure.Migrations
{
    /// <summary>
    /// The server-side half of a browser session, so that signing out revokes
    /// rather than merely asking the browser to forget. An Identity cookie is
    /// self-contained: without a row to check against, a copy taken before
    /// sign-out keeps authenticating for the rest of the sliding window. One row
    /// per OPEN session — per session, not per user, which is what lets one
    /// workstation be signed out while the same person stays signed in
    /// elsewhere. Deleted on sign-out, reclaimed after expiry, and cascaded with
    /// the account.
    /// </summary>
    public partial class UserSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_sessions_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_user_sessions_UserId_ExpiresAt",
                table: "user_sessions",
                columns: new[] { "UserId", "ExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_sessions");
        }
    }
}
