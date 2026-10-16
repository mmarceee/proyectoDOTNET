using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Modules.Seguimiento.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AvisosDeRuta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "seguimiento");

            migrationBuilder.CreateTable(
                name: "avisos_ruta",
                schema: "seguimiento",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    RepartidorId = table.Column<Guid>(type: "uuid", nullable: false),
                    RutaId = table.Column<Guid>(type: "uuid", nullable: false),
                    FechaRuta = table.Column<DateOnly>(type: "date", nullable: false),
                    DespachadaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_avisos_ruta", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "seguimiento",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Consumidor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProcesadoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbox_messages", x => new { x.MessageId, x.Consumidor });
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "seguimiento",
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
                name: "IX_avisos_ruta_OperadorId_RepartidorId_DespachadaEn",
                schema: "seguimiento",
                table: "avisos_ruta",
                columns: new[] { "OperadorId", "RepartidorId", "DespachadaEn" });

            migrationBuilder.CreateIndex(
                name: "IX_avisos_ruta_OperadorId_RutaId_Revision",
                schema: "seguimiento",
                table: "avisos_ruta",
                columns: new[] { "OperadorId", "RutaId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_OcurridoEn",
                schema: "seguimiento",
                table: "outbox_messages",
                column: "OcurridoEn",
                filter: "\"PublicadoEn\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "avisos_ruta",
                schema: "seguimiento");

            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "seguimiento");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "seguimiento");
        }
    }
}
