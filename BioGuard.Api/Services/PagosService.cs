using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using BioGuard.Api.Config;
using BioGuard.Api.Models;

namespace BioGuard.Api.Services;

public class PagosService
{
    private readonly IMongoDbContext _db;
    private readonly ILogger<PagosService> _logger;

    public PagosService(IMongoDbContext db, ILogger<PagosService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Pago?> CrearSesionAsync(string usuarioId, Plan plan, PaymentSessionResult gatewayResult, string procesador)
    {
        _logger.LogInformation("Creando sesión de pago para usuario {UsuarioId}, plan {Plan}, procesador {Procesador}", usuarioId, plan.Nombre, procesador);

        var pago = new Pago
        {
            UsuarioWebId = usuarioId,
            Monto = plan.Precio,
            Moneda = plan.PrecioMoneda,
            PlanId = plan.Id,
            Estado = "pendiente",
            FechaPago = DateTime.UtcNow,
            MetodoPago = procesador,
            SesionUrl = gatewayResult.SessionUrl,
            StripeSubscriptionId = gatewayResult.SubscriptionId
        };

        if (procesador == "stripe")
        {
            pago.StripeSessionId = gatewayResult.SessionId;
        }
        else if (procesador == "paypal")
        {
            pago.PayPalOrderId = gatewayResult.SessionId;
        }

        await _db.Pagos.InsertOneAsync(pago);
        _logger.LogInformation("Sesión de pago creada con ID {PagoId}", pago.Id);
        return pago;
    }

    public async Task<List<Pago>> ObtenerHistorialAsync(string usuarioId)
    {
        _logger.LogInformation("Obteniendo historial de pagos para usuario {UsuarioId}", usuarioId);
        var filter = Builders<Pago>.Filter.Eq(p => p.UsuarioWebId, usuarioId);
        var sort = Builders<Pago>.Sort.Descending(p => p.FechaPago);
        return await _db.FindToListAsync(_db.Pagos, filter, sort);
    }

    public async Task<Pago?> ObtenerPorIdAsync(string pagoId)
    {
        _logger.LogInformation("Buscando pago {PagoId}", pagoId);
        return await _db.FindFirstOrDefaultAsync(_db.Pagos, p => p.Id == pagoId);
    }

    public async Task<bool> CancelarAsync(string usuarioId, IPaymentGateway gateway)
    {
        _logger.LogInformation("Cancelando pago para usuario {UsuarioId}", usuarioId);
        var filter = Builders<Pago>.Filter.And(
            Builders<Pago>.Filter.Eq(p => p.UsuarioWebId, usuarioId),
            Builders<Pago>.Filter.Eq(p => p.Estado, "completado"));
        var sort = Builders<Pago>.Sort.Descending(p => p.FechaPago);
        var pago = await _db.FindFirstOrDefaultAsync(_db.Pagos, filter, sort);

        if (pago == null)
        {
            _logger.LogWarning("No se encontró pago completado para cancelar, usuario {UsuarioId}", usuarioId);
            return false;
        }

        var subId = pago.StripeSubscriptionId;
        if (!string.IsNullOrEmpty(subId))
        {
            await gateway.CancelSubscriptionAsync(subId);
        }

        var update = Builders<Pago>.Update.Set(p => p.Estado, "cancelado");
        var result = await _db.Pagos.UpdateOneAsync(p => p.Id == pago.Id, update);
        _logger.LogInformation("Pago {PagoId} cancelado", pago.Id);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> CancelarSuscripcionPorUsuarioAsync(string usuarioId, IPaymentGateway stripeGateway, IPaymentGateway paypalGateway)
    {
        _logger.LogInformation("Cancelando suscripción para usuario {UsuarioId}", usuarioId);
        var filter = Builders<Pago>.Filter.And(
            Builders<Pago>.Filter.Eq(p => p.UsuarioWebId, usuarioId),
            Builders<Pago>.Filter.Eq(p => p.Estado, "completado"));
        var sort = Builders<Pago>.Sort.Descending(p => p.FechaPago);
        var pago = await _db.FindFirstOrDefaultAsync(_db.Pagos, filter, sort);

        if (pago == null)
        {
            _logger.LogWarning("No se encontró suscripción activa para usuario {UsuarioId}", usuarioId);
            return false;
        }

        if (pago.MetodoPago == "paypal")
        {
            _logger.LogWarning("PayPal está en mantenimiento - no se puede cancelar suscripción PayPal para usuario {UsuarioId}", usuarioId);
            return false;
        }

        if (!string.IsNullOrEmpty(pago.StripeSubscriptionId))
            await stripeGateway.CancelSubscriptionAsync(pago.StripeSubscriptionId);

        var update = Builders<Pago>.Update.Set(p => p.Estado, "cancelado");
        var result = await _db.Pagos.UpdateOneAsync(p => p.Id == pago.Id, update);
        _logger.LogInformation("Suscripción cancelada para usuario {UsuarioId}", usuarioId);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> ActualizarPagoCompletadoAsync(string sessionId, string? customerId, string? subscriptionId)
    {
        var filter = Builders<Pago>.Filter.Eq(p => p.StripeSessionId, sessionId);
        var update = Builders<Pago>.Update
            .Set(p => p.Estado, "completado")
            .Set(p => p.StripeCustomerId, customerId);

        if (!string.IsNullOrEmpty(subscriptionId))
        {
            update = update.Set(p => p.StripeSubscriptionId, subscriptionId);
        }

        var result = await _db.Pagos.UpdateOneAsync(filter, update);
        if (result.ModifiedCount > 0)
        {
            _logger.LogInformation("Pago completado: {SessionId}", sessionId);
            return true;
        }
        _logger.LogWarning("No se encontró pago pendiente con sessionId: {SessionId}", sessionId);
        return false;
    }

    public async Task<(Pago? pago, UsuarioWeb? usuario)> ObtenerPagoYUsuarioPorSessionIdAsync(string sessionId)
    {
        var pago = await _db.FindFirstOrDefaultAsync(_db.Pagos, p => p.StripeSessionId == sessionId);
        if (pago == null) return (null, null);

        var usuario = await _db.FindFirstOrDefaultAsync(_db.UsuariosWeb, u => u.Id == pago.UsuarioWebId);
        return (pago, usuario);
    }

    public async Task ActualizarPlanUsuarioAsync(string usuarioId, string planId)
    {
        var update = Builders<UsuarioWeb>.Update.Set(u => u.PlanId, planId);
        await _db.UsuariosWeb.UpdateOneAsync(u => u.Id == usuarioId, update);
        _logger.LogInformation("Plan {PlanId} asignado al usuario {UsuarioId}", planId, usuarioId);
    }

    public async Task<bool> EventoYaProcesadoAsync(string eventoId)
    {
        if (string.IsNullOrEmpty(eventoId)) return false;
        var exists = await _db.FindFirstOrDefaultAsync(_db.EventosProcesados, p => p.Id == eventoId);
        return exists != null;
    }

    public async Task<bool> ActualizarPagoPayPalAsync(string orderId)
    {
        var filter = Builders<Pago>.Filter.Eq(p => p.PayPalOrderId, orderId);
        var update = Builders<Pago>.Update.Set(p => p.Estado, "completado");
        var result = await _db.Pagos.UpdateOneAsync(filter, update);
        if (result.ModifiedCount > 0)
        {
            _logger.LogInformation("Pago PayPal completado: {OrderId}", orderId);
            return true;
        }
        _logger.LogWarning("No se encontró pago PayPal pendiente con orderId: {OrderId}", orderId);
        return false;
    }

    public async Task<(Pago? pago, UsuarioWeb? usuario)> ObtenerPagoYUsuarioPorPayPalOrderIdAsync(string orderId)
    {
        var pago = await _db.FindFirstOrDefaultAsync(_db.Pagos, p => p.PayPalOrderId == orderId);
        if (pago == null) return (null, null);

        var usuario = await _db.FindFirstOrDefaultAsync(_db.UsuariosWeb, u => u.Id == pago.UsuarioWebId);
        return (pago, usuario);
    }

    public async Task RegistrarEventoIdAsync(string pagoId, string eventoId)
    {
        var update = Builders<Pago>.Update.Set(p => p.EventoId, eventoId);
        await _db.Pagos.UpdateOneAsync(p => p.Id == pagoId, update);
    }

    public async Task<bool> RegistrarEventoIdSiNoExisteAsync(string eventoId)
    {
        try
        {
            var registro = new EventoProcesado { Id = eventoId, Fecha = DateTime.UtcNow };
            await _db.EventosProcesados.InsertOneAsync(registro);
            return true;
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    public async Task<bool> DowngradeToFreeAsync(string usuarioId)
    {
        var freeAliases = PlanCatalog.Aliases(PlanCatalog.Free);
        var freePlan = await _db.FindFirstOrDefaultAsync(
            _db.Planes, p => freeAliases.Contains(p.Nombre) && p.Activo);
        if (freePlan == null)
        {
            _logger.LogError("No se encontró plan Gratis para downgrade del usuario {UsuarioId}", usuarioId);
            return false;
        }

        var update = Builders<UsuarioWeb>.Update.Set(u => u.PlanId, freePlan.Id);
        await _db.UsuariosWeb.UpdateOneAsync(u => u.Id == usuarioId, update);
        _logger.LogInformation("Usuario {UsuarioId} downgraded to Free plan", usuarioId);
        return true;
    }

    public async Task RevocarTokensCuidadorAsync(string pacienteId)
    {
        var cuidadores = await _db.FindToListAsync(_db.Cuidadores, c => c.PacienteId == pacienteId);
        foreach (var cuidador in cuidadores)
        {
            await _db.RefreshTokens.DeleteManyAsync(t => t.UsuarioId == cuidador.Id);
            _logger.LogInformation("Tokens revocados para cuidador {CuidadorId}", cuidador.Id);
        }
    }

    public async Task<long> OcultarHistorialAntiguoAsync(string usuarioId, int mesesRetencion)
    {
        var corte = DateTime.UtcNow.AddMonths(-mesesRetencion);
        var paciente = await _db.FindFirstOrDefaultAsync(_db.Pacientes, p => p.UsuarioWebId == usuarioId);
        if (paciente == null)
        {
            _logger.LogWarning("No se encontró paciente para usuario {UsuarioId} al ocultar historial", usuarioId);
            return 0;
        }
        var filter = Builders<LecturaSensor>.Filter.And(
            Builders<LecturaSensor>.Filter.Eq(r => r.Meta.PacienteId, paciente.Id),
            Builders<LecturaSensor>.Filter.Lt(r => r.Timestamp, corte));
        var update = Builders<LecturaSensor>.Update.Set(r => r.Oculto, true);
        var result = await _db.LecturasSensores.UpdateManyAsync(filter, update);
        _logger.LogInformation("Historial ocultado para paciente {PacienteId}: {Count} registros", paciente.Id, result.ModifiedCount);
        return result.ModifiedCount;
    }
}
