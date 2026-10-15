using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Modules.Administracion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UsuarioDelRepartidor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "UsuarioId",
                schema: "administracion",
                table: "Repartidores",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Repartidores_UsuarioId",
                schema: "administracion",
                table: "Repartidores",
                column: "UsuarioId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Repartidores_UsuarioId",
                schema: "administracion",
                table: "Repartidores");

            migrationBuilder.DropColumn(
                name: "UsuarioId",
                schema: "administracion",
                table: "Repartidores");
        }
    }
}
