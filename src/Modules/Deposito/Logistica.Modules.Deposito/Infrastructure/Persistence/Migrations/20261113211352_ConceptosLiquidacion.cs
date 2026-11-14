using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Modules.Deposito.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConceptosLiquidacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_LineasLiquidacion_EnvioId",
                schema: "deposito",
                table: "LineasLiquidacion");

            migrationBuilder.AddColumn<Guid>(
                name: "IdDeOrigen",
                schema: "deposito",
                table: "LineasLiquidacion",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "Origen",
                schema: "deposito",
                table: "LineasLiquidacion",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            // Las líneas anteriores eran todas la tarifa base de un envío: su concepto de origen es el propio envío, así
            // esos envíos no se vuelven a cobrar (cierre técnico, D8).
            migrationBuilder.Sql("""
                UPDATE deposito."LineasLiquidacion" SET "Origen" = 'BaseEnvio', "IdDeOrigen" = "EnvioId";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_LineasLiquidacion_EnvioId",
                schema: "deposito",
                table: "LineasLiquidacion",
                column: "EnvioId");

            migrationBuilder.CreateIndex(
                name: "UX_LineasLiquidacion_Origen",
                schema: "deposito",
                table: "LineasLiquidacion",
                columns: new[] { "OperadorId", "Origen", "IdDeOrigen" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LineasLiquidacion_EnvioId",
                schema: "deposito",
                table: "LineasLiquidacion");

            migrationBuilder.DropIndex(
                name: "UX_LineasLiquidacion_Origen",
                schema: "deposito",
                table: "LineasLiquidacion");

            migrationBuilder.DropColumn(
                name: "IdDeOrigen",
                schema: "deposito",
                table: "LineasLiquidacion");

            migrationBuilder.DropColumn(
                name: "Origen",
                schema: "deposito",
                table: "LineasLiquidacion");

            migrationBuilder.CreateIndex(
                name: "UX_LineasLiquidacion_EnvioId",
                schema: "deposito",
                table: "LineasLiquidacion",
                column: "EnvioId",
                unique: true);
        }
    }
}
