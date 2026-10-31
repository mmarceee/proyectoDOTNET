using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.BuildingBlocks.Infrastructure.Messaging.Fallidos.Migrations
{
    /// <inheritdoc />
    public partial class MensajesFallidos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "mensajeria");

            migrationBuilder.CreateTable(
                name: "mensajes_fallidos",
                schema: "mensajeria",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComercioId = table.Column<Guid>(type: "uuid", nullable: true),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    ColaOrigen = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Tipo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OcurridoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TraceParent = table.Column<string>(type: "character varying(55)", maxLength: 55, nullable: true),
                    Contenido = table.Column<string>(type: "text", nullable: false),
                    Intentos = table.Column<int>(type: "integer", nullable: false),
                    UltimoError = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Historial = table.Column<string[]>(type: "text[]", nullable: false),
                    Estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RegistradoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActualizadoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResueltoPor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ResueltoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MotivoDescarte = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mensajes_fallidos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "mensajeria",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OcurridoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComercioId = table.Column<Guid>(type: "uuid", nullable: true),
                    TraceParent = table.Column<string>(type: "character varying(55)", maxLength: 55, nullable: true),
                    Contenido = table.Column<string>(type: "text", nullable: false),
                    PublicadoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_mensajes_fallidos_MessageId_ColaOrigen",
                schema: "mensajeria",
                table: "mensajes_fallidos",
                columns: new[] { "MessageId", "ColaOrigen" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mensajes_fallidos_OperadorId_Estado_ActualizadoEn",
                schema: "mensajeria",
                table: "mensajes_fallidos",
                columns: new[] { "OperadorId", "Estado", "ActualizadoEn" });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_OcurridoEn",
                schema: "mensajeria",
                table: "outbox_messages",
                column: "OcurridoEn",
                filter: "\"PublicadoEn\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mensajes_fallidos",
                schema: "mensajeria");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "mensajeria");
        }
    }
}
