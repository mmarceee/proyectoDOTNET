using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Modules.Seguimiento.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NotificacionesDestinatario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EmailDestinatario",
                schema: "seguimiento",
                table: "envios_seguidos",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NombreDestinatario",
                schema: "seguimiento",
                table: "envios_seguidos",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "identidades_operador",
                schema: "seguimiento",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ColorPrimario = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ColorSecundario = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EmailContacto = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    TelefonoContacto = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ActualizadaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identidades_operador", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "notificaciones",
                schema: "seguimiento",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComercioId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvioId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Canal = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Tipo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    EstadoEnvio = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Destino = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    Estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreadaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EnviadaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Intentos = table.Column<int>(type: "integer", nullable: false),
                    UltimoError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notificaciones", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_identidades_operador_OperadorId",
                schema: "seguimiento",
                table: "identidades_operador",
                column: "OperadorId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notificaciones_EventoId",
                schema: "seguimiento",
                table: "notificaciones",
                column: "EventoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notificaciones_OperadorId_EnvioId",
                schema: "seguimiento",
                table: "notificaciones",
                columns: new[] { "OperadorId", "EnvioId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "identidades_operador",
                schema: "seguimiento");

            migrationBuilder.DropTable(
                name: "notificaciones",
                schema: "seguimiento");

            migrationBuilder.DropColumn(
                name: "EmailDestinatario",
                schema: "seguimiento",
                table: "envios_seguidos");

            migrationBuilder.DropColumn(
                name: "NombreDestinatario",
                schema: "seguimiento",
                table: "envios_seguidos");
        }
    }
}
