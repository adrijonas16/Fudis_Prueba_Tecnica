# Fudis Prueba técnica del postulante

Desarrollador Senior Backend · Confirmación de pagos y asignación de pedidos

Evalúa la implementación existente y entrega una mejora que puedas explicar y defender. Fudis conecta clientes, restaurantes y repartidores; la confirmación de un pago habilita operaciones que afectan dinero y pedidos.

### Condiciones de la evaluación

- Stack de referencia: C# / .NET, Entity Framework y SQL Server. No se requiere interfaz web ni integración con una pasarela real.

- Duración total: 90–120 minutos, incluidos 60–90 minutos de análisis e implementación y 30 minutos de defensa técnica.

- Uso de IA permitido. Puedes consultar documentación, asistentes y otras herramientas. Eres responsable de validar y comprender lo que entregues.

- Prioriza una solución acotada y verificable. Si falta información, declara supuestos y explica sus consecuencias.

### Caso de negocio

Cuando un cliente completa el pago de un pedido, el backend recibe una notificación de la pasarela. El endpoint actual marca el pedido como pagado, registra una ganancia del repartidor equivalente al 70 % de la tarifa de envío y notifica al restaurante.

El código ha superado las pruebas funcionales realizadas hasta ahora. Tu responsabilidad es determinar si está listo para producción y proponer los cambios que consideres necesarios.

### Información disponible

Order contiene Id, Status, DriverId y DeliveryFee. DriverEarning contiene OrderId, DriverId y Amount. PaymentNotification contiene al menos OrderId. El fragmento no incluye DTO completos, esquema, configuración ni contrato de la pasarela. Puedes completar esos elementos y justificar tus decisiones; no se presume ninguna garantía adicional no declarada.

## Código inicial

Conserva este fragmento como referencia del comportamiento actual. Implementa tu propuesta por separado.

```csharp
[HttpPost("confirm-payment")]
public async Task<IActionResult> ConfirmPayment(
    PaymentNotification request)
{
    var order = await _db.Orders
        .FindAsync(request.OrderId);

    if (order == null)
        return NotFound();

    if (order.Status == "PAID")
        return Ok();

    order.Status = "PAID";

    _db.DriverEarnings.Add(new DriverEarning
    {
        OrderId = order.Id,
        DriverId = order.DriverId,
        Amount = order.DeliveryFee * 0.70m
    });

    await _db.SaveChangesAsync();

    await _notificationService
        .NotifyRestaurant(order.Id);

    return Ok();
}
```

## Objetivos y entrega

### Tu desafío

- Identifica los problemas técnicos y de negocio que consideres relevantes. Ordénalos por impacto y describe cómo podrían manifestarse.

- Propón e implementa una versión mejorada del flujo. Incluye los cambios de persistencia necesarios y explica las garantías que ofrece.

- Diseña y ejecuta las pruebas que consideres necesarias. Distingue las pruebas realizadas de las propuestas pendientes.

- Explica qué sucede si la pasarela envía múltiples notificaciones del mismo pago.

- Explica cómo mantienes la consistencia entre el pago, las ganancias del repartidor y la notificación al restaurante.

### Entregables

- Código fuente y cambios de base de datos, mediante repositorio o archivo comprimido. Incluye instrucciones de ejecución y versiones utilizadas.

- README breve con hallazgos, supuestos, decisiones, limitaciones y trabajo pendiente. Documenta el contrato de entrada que propones.

- Pruebas relevantes y evidencia de sus resultados. Si no completas un componente, aporta pseudocódigo o un diseño concreto y señálalo expresamente.

- Nota breve sobre las herramientas de IA utilizadas, para qué las usaste y qué verificaste o corregiste. No se exige compartir conversaciones privadas.

### Defensa posterior

Dispondrás de 30 minutos para recorrer el flujo, justificar las decisiones, explicar resultados y responder a variaciones del escenario. Podremos pedir una pequeña modificación o demostración sobre tu entrega.

Se evaluarán el criterio técnico, la protección de las operaciones del negocio, la calidad de las pruebas y la claridad al explicar las garantías y límites de la solución. La cantidad de código no sustituye esas capacidades.
