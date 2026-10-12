using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Modules.Envios.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OutboxPorModulo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_outbox_messages_PublicadoEn_OcurridoEn",
                schema: "envios",
                table: "outbox_messages");

            migrationBuilder.AlterColumn<string>(
                name: "Tipo",
                schema: "envios",
                table: "outbox_messages",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<Guid>(
                name: "ComercioId",
                schema: "envios",
                table: "outbox_messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TraceParent",
                schema: "envios",
                table: "outbox_messages",
                type: "character varying(55)",
                maxLength: 55,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_OcurridoEn",
                schema: "envios",
                table: "outbox_messages",
                column: "OcurridoEn",
                filter: "\"PublicadoEn\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_outbox_messages_OcurridoEn",
                schema: "envios",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "ComercioId",
                schema: "envios",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "TraceParent",
                schema: "envios",
                table: "outbox_messages");

            migrationBuilder.AlterColumn<string>(
                name: "Tipo",
                schema: "envios",
                table: "outbox_messages",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_PublicadoEn_OcurridoEn",
                schema: "envios",
                table: "outbox_messages",
                columns: new[] { "PublicadoEn", "OcurridoEn" });
        }
    }
}
