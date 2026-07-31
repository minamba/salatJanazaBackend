using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using QabrWebApp.Dal.Entities;

#nullable disable

namespace QabrWebApp.Dal.Migrations
{
    [DbContext(typeof(QabrWebAppDatabaseContext))]
    [Migration("20260730120000_AddPlatformToUtilisateur")]
    public partial class AddPlatformToUtilisateur : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Platform",
                table: "Utilisateurs",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Platform",
                table: "Utilisateurs");
        }
    }
}
