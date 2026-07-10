using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
{
    [DbContext(typeof(SqliteServerDbContext))]
    [Migration("20260628000000_KrittersBloodTypePreference")]
    public partial class KrittersBloodTypePreference : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "blood_type",
                table: "profile",
                type: "TEXT",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "blood_type",
                table: "profile");
        }
    }
}
