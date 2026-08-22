using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QabrWebApp.Dal.Migrations
{
    [Migration("20260817000004_AddCommentaireDateModification")]
    public partial class AddCommentaireDateModification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DateModification",
                table: "CommentairesJanaza",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "DateModification", table: "CommentairesJanaza");
        }
    }
}
