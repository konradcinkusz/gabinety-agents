using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gabinety.Directory.Persistence.Migrations
{
    /// <summary>
    /// E-04: one provider account may own several agents. The unique index on <c>user_id</c> becomes an ordinary one, and the unique partial index on the linked GitHub login
    /// becomes an ordinary partial one: the agents of one account may share a GitHub profile, and "one profile, one account" is checked by the application (VerificationFeature).
    /// Slug stays unique. Down restores both unique indexes and fails when an account already owns two agents (merge or remove them first).
    /// </summary>
    public partial class ManyAgentsPerProvider : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_practitioners_user_id", table: "practitioners");
            migrationBuilder.CreateIndex(name: "IX_practitioners_user_id", table: "practitioners", column: "user_id");

            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_practitioners_github_login_linked");
            migrationBuilder.Sql("""CREATE INDEX ix_practitioners_github_login_linked ON practitioners (lower(github_login)) WHERE github_linked;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_practitioners_github_login_linked");
            migrationBuilder.Sql("""CREATE UNIQUE INDEX ux_practitioners_github_login_linked ON practitioners (lower(github_login)) WHERE github_linked;""");

            migrationBuilder.DropIndex(name: "IX_practitioners_user_id", table: "practitioners");
            migrationBuilder.CreateIndex(name: "IX_practitioners_user_id", table: "practitioners", column: "user_id", unique: true);
        }
    }
}
