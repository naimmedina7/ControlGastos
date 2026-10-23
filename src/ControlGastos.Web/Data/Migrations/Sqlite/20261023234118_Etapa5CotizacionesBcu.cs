using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlGastos.Web.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class Etapa5CotizacionesBcu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CotizacionesBcu",
                columns: table => new
                {
                    Mes = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    UsdEnPesos = table.Column<decimal>(type: "TEXT", precision: 12, scale: 4, nullable: false),
                    Definitiva = table.Column<bool>(type: "INTEGER", nullable: false),
                    ActualizadoEn = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CotizacionesBcu", x => x.Mes);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CotizacionesBcu");
        }
    }
}
