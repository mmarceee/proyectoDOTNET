using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Modules.Envios.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "envios");

            migrationBuilder.CreateSequence(
                name: "numero_envio",
                schema: "envios");

            migrationBuilder.CreateTable(
                name: "Envios",
                schema: "envios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComercioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Numero = table.Column<string>(type: "text", nullable: false),
                    Estado = table.Column<string>(type: "text", nullable: false),
                    MontoTarifa = table.Column<decimal>(type: "numeric", nullable: false),
                    CreadoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Destinatario_Documento = table.Column<string>(type: "text", nullable: true),
                    Destinatario_Email = table.Column<string>(type: "text", nullable: true),
                    Destinatario_Nombre = table.Column<string>(type: "text", nullable: false),
                    Destinatario_Telefono = table.Column<string>(type: "text", nullable: false),
                    Direccion_Calle = table.Column<string>(type: "text", nullable: false),
                    Direccion_CodigoPostal = table.Column<string>(type: "text", nullable: false),
                    Direccion_Departamento = table.Column<string>(type: "text", nullable: false),
                    Direccion_Localidad = table.Column<string>(type: "text", nullable: false),
                    Direccion_Numero = table.Column<string>(type: "text", nullable: false),
                    Direccion_Referencia = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Envios", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Bultos",
                schema: "envios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvioId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComercioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Codigo = table.Column<string>(type: "text", nullable: false),
                    PesoKg = table.Column<decimal>(type: "numeric", nullable: false),
                    LargoCm = table.Column<decimal>(type: "numeric", nullable: false),
                    AnchoCm = table.Column<decimal>(type: "numeric", nullable: false),
                    AltoCm = table.Column<decimal>(type: "numeric", nullable: false),
                    MontoTarifa = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bultos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Bultos_Envios_EnvioId",
                        column: x => x.EnvioId,
                        principalSchema: "envios",
                        principalTable: "Envios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EventosEnvio",
                schema: "envios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvioId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComercioId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstadoAnterior = table.Column<string>(type: "text", nullable: true),
                    EstadoNuevo = table.Column<string>(type: "text", nullable: false),
                    OcurridoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Origen = table.Column<string>(type: "text", nullable: false),
                    ResponsableId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventosEnvio", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventosEnvio_Envios_EnvioId",
                        column: x => x.EnvioId,
                        principalSchema: "envios",
                        principalTable: "Envios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Bultos_EnvioId",
                schema: "envios",
                table: "Bultos",
                column: "EnvioId");

            migrationBuilder.CreateIndex(
                name: "IX_Bultos_OperadorId_Codigo",
                schema: "envios",
                table: "Bultos",
                columns: new[] { "OperadorId", "Codigo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Envios_OperadorId_Numero",
                schema: "envios",
                table: "Envios",
                columns: new[] { "OperadorId", "Numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventosEnvio_EnvioId",
                schema: "envios",
                table: "EventosEnvio",
                column: "EnvioId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Bultos",
                schema: "envios");

            migrationBuilder.DropTable(
                name: "EventosEnvio",
                schema: "envios");

            migrationBuilder.DropTable(
                name: "Envios",
                schema: "envios");

            migrationBuilder.DropSequence(
                name: "numero_envio",
                schema: "envios");
        }
    }
}
