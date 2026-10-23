using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ControlGastos.Web.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class Etapa2GastosFijos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DiaCierre",
                table: "Tarjetas",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GastoRecurrenteId",
                table: "Gastos",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GastosRecurrentes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Descripcion = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    TarjetaId = table.Column<int>(type: "integer", nullable: true),
                    CategoriaId = table.Column<int>(type: "integer", nullable: true),
                    Moneda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Monto = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    DiaDelMes = table.Column<int>(type: "integer", nullable: false),
                    Desde = table.Column<DateOnly>(type: "date", nullable: false),
                    Hasta = table.Column<DateOnly>(type: "date", nullable: true),
                    Activo = table.Column<bool>(type: "boolean", nullable: false),
                    GeneradoHasta = table.Column<DateOnly>(type: "date", nullable: true),
                    Notas = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreadoEn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GastosRecurrentes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GastosRecurrentes_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GastosRecurrentes_Categorias_CategoriaId",
                        column: x => x.CategoriaId,
                        principalTable: "Categorias",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_GastosRecurrentes_Tarjetas_TarjetaId",
                        column: x => x.TarjetaId,
                        principalTable: "Tarjetas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Gastos_GastoRecurrenteId_PrimerPeriodo",
                table: "Gastos",
                columns: new[] { "GastoRecurrenteId", "PrimerPeriodo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GastosRecurrentes_CategoriaId",
                table: "GastosRecurrentes",
                column: "CategoriaId");

            migrationBuilder.CreateIndex(
                name: "IX_GastosRecurrentes_TarjetaId",
                table: "GastosRecurrentes",
                column: "TarjetaId");

            migrationBuilder.CreateIndex(
                name: "IX_GastosRecurrentes_UserId_Activo",
                table: "GastosRecurrentes",
                columns: new[] { "UserId", "Activo" });

            migrationBuilder.AddForeignKey(
                name: "FK_Gastos_GastosRecurrentes_GastoRecurrenteId",
                table: "Gastos",
                column: "GastoRecurrenteId",
                principalTable: "GastosRecurrentes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Gastos_GastosRecurrentes_GastoRecurrenteId",
                table: "Gastos");

            migrationBuilder.DropTable(
                name: "GastosRecurrentes");

            migrationBuilder.DropIndex(
                name: "IX_Gastos_GastoRecurrenteId_PrimerPeriodo",
                table: "Gastos");

            migrationBuilder.DropColumn(
                name: "DiaCierre",
                table: "Tarjetas");

            migrationBuilder.DropColumn(
                name: "GastoRecurrenteId",
                table: "Gastos");
        }
    }
}
