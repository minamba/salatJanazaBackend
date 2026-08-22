using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QabrWebApp.Dal.Migrations
{
    /// <inheritdoc />
    public partial class AddCommentaireColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "UtilisateurId",
                table: "CommentairesJanaza",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EstCache",
                table: "CommentairesJanaza",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "UtilisateurId", table: "CommentairesJanaza");
            migrationBuilder.DropColumn(name: "EstCache", table: "CommentairesJanaza");
        }
    }
}
