using Logistica.SharedKernel;

namespace Logistica.BuildingBlocks.Infrastructure.Tenancy;

// Inquilino fijado a mano, para cuando no hay un request del que leerlo: el seed de cada operador,
// cada mensaje que procesa un consumidor y las pruebas (ADR-0002, secciones 2.5 y 2.8).
public sealed record InquilinoFijo(Guid? OperadorId, Guid? ComercioId) : ICurrentTenant;
