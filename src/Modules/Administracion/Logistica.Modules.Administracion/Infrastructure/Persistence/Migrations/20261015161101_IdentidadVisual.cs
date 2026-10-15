using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Modules.Administracion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IdentidadVisual : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Identidad_ColorPrimario",
                schema: "administracion",
                table: "Operadores",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "#0D6EFD");

            migrationBuilder.AddColumn<string>(
                name: "Identidad_ColorSecundario",
                schema: "administracion",
                table: "Operadores",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "#6C757D");

            migrationBuilder.AddColumn<string>(
                name: "Identidad_EmailContacto",
                schema: "administracion",
                table: "Operadores",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "Identidad_LogoArchivoId",
                schema: "administracion",
                table: "Operadores",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Identidad_TelefonoContacto",
                schema: "administracion",
                table: "Operadores",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Logos",
                schema: "administracion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    TipoContenido = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Contenido = table.Column<byte[]>(type: "bytea", maxLength: 1048576, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Logos", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Logos",
                schema: "administracion");

            migrationBuilder.DropColumn(
                name: "Identidad_ColorPrimario",
                schema: "administracion",
                table: "Operadores");

            migrationBuilder.DropColumn(
                name: "Identidad_ColorSecundario",
                schema: "administracion",
                table: "Operadores");

            migrationBuilder.DropColumn(
                name: "Identidad_EmailContacto",
                schema: "administracion",
                table: "Operadores");

            migrationBuilder.DropColumn(
                name: "Identidad_LogoArchivoId",
                schema: "administracion",
                table: "Operadores");

            migrationBuilder.DropColumn(
                name: "Identidad_TelefonoContacto",
                schema: "administracion",
                table: "Operadores");
        }
    }
}
