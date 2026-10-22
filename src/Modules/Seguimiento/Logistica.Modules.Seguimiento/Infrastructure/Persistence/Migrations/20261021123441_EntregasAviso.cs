using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Modules.Seguimiento.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EntregasAviso : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "entregas_aviso",
                schema: "seguimiento",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComercioId = table.Column<Guid>(type: "uuid", nullable: false),
                    SuscripcionAvisoId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventoId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvioId = table.Column<Guid>(type: "uuid", nullable: false),
                    NumeroEnvio = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EstadoEnvio = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Tipo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    FechaEvento = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Estado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entregas_aviso", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "envios_seguidos",
                schema: "seguimiento",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComercioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Numero = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_envios_seguidos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "intentos_aviso",
                schema: "seguimiento",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComercioId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntregaAvisoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Numero = table.Column<int>(type: "integer", nullable: false),
                    EnviadoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Exitoso = table.Column<bool>(type: "boolean", nullable: false),
                    CodigoRespuesta = table.Column<int>(type: "integer", nullable: true),
                    Error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_intentos_aviso", x => x.Id);
                    table.ForeignKey(
                        name: "FK_intentos_aviso_entregas_aviso_EntregaAvisoId",
                        column: x => x.EntregaAvisoId,
                        principalSchema: "seguimiento",
                        principalTable: "entregas_aviso",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_entregas_aviso_EventoId_SuscripcionAvisoId",
                schema: "seguimiento",
                table: "entregas_aviso",
                columns: new[] { "EventoId", "SuscripcionAvisoId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_entregas_aviso_OperadorId_ComercioId_Estado_FechaEvento",
                schema: "seguimiento",
                table: "entregas_aviso",
                columns: new[] { "OperadorId", "ComercioId", "Estado", "FechaEvento" });

            migrationBuilder.CreateIndex(
                name: "IX_intentos_aviso_EntregaAvisoId_Numero",
                schema: "seguimiento",
                table: "intentos_aviso",
                columns: new[] { "EntregaAvisoId", "Numero" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "envios_seguidos",
                schema: "seguimiento");

            migrationBuilder.DropTable(
                name: "intentos_aviso",
                schema: "seguimiento");

            migrationBuilder.DropTable(
                name: "entregas_aviso",
                schema: "seguimiento");
        }
    }
}
