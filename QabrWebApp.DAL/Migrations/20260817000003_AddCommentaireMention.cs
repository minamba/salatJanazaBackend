using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QabrWebApp.Dal.Migrations
{
    public partial class AddCommentaireMention : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MentionNom",
                table: "CommentairesJanaza",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "MentionNom", table: "CommentairesJanaza");
        }
    }
}
