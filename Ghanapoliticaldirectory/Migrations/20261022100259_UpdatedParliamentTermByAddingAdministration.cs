using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ghanapoliticaldirectory.Migrations
{
    /// <inheritdoc />
    public partial class UpdatedParliamentTermByAddingAdministration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AdministrationId",
                table: "ParliamentaryTerms",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ParliamentaryTerms_AdministrationId",
                table: "ParliamentaryTerms",
                column: "AdministrationId");

            migrationBuilder.AddForeignKey(
                name: "FK_ParliamentaryTerms_Administrations_AdministrationId",
                table: "ParliamentaryTerms",
                column: "AdministrationId",
                principalTable: "Administrations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ParliamentaryTerms_Administrations_AdministrationId",
                table: "ParliamentaryTerms");

            migrationBuilder.DropIndex(
                name: "IX_ParliamentaryTerms_AdministrationId",
                table: "ParliamentaryTerms");

            migrationBuilder.DropColumn(
                name: "AdministrationId",
                table: "ParliamentaryTerms");
        }
    }
}
