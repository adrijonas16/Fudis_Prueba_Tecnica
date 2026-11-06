# Simula la pasarela: envía una notificación firmada con HMAC-SHA256 a la API.
# Uso: ./scripts/send-notification.ps1 -OrderId 1 [-EventId evt_1] [-Amount 45000.00] [-PaymentId pay_1] [-EventType payment.succeeded]
param(
    [Parameter(Mandatory = $true)][int]$OrderId,
    [string]$EventId = "evt_$([DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds())",
    [string]$Amount = "45000.00",
    [string]$PaymentId = "pay_demo_$OrderId",
    [string]$EventType = "payment.succeeded",
    [string]$ApiUrl = $(if ($env:API_URL) { $env:API_URL } else { "http://localhost:5080" }),
    [string]$Secret = $(if ($env:WEBHOOK_SECRET) { $env:WEBHOOK_SECRET } else { "dev-only-secret-change-me" })
)

$occurredAt = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
$body = "{`"eventId`":`"$EventId`",`"eventType`":`"$EventType`",`"paymentId`":`"$PaymentId`",`"orderId`":$OrderId,`"amount`":$Amount,`"currency`":`"COP`",`"occurredAt`":`"$occurredAt`"}"
$ts = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()

$hmac = [System.Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($Secret))
$sig = ([BitConverter]::ToString($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes("$ts.$body"))) -replace '-', '').ToLower()

Write-Host ">> $body"
try {
    $response = Invoke-WebRequest -UseBasicParsing -Method Post -Uri "$ApiUrl/api/payments/confirm-payment" -ContentType "application/json" `
        -Headers @{ "X-Gateway-Signature" = "t=$ts,v1=$sig" } -Body $body
    Write-Host "<< HTTP $($response.StatusCode) $($response.Content)"
}
catch [System.Net.WebException] {
    # Windows PowerShell 5.1 lanza excepción en 4xx/5xx: se muestra el código igualmente.
    $code = [int]$_.Exception.Response.StatusCode
    Write-Host "<< HTTP $code"
}
catch {
    # PowerShell 7+
    if ($_.Exception.Response) { Write-Host "<< HTTP $([int]$_.Exception.Response.StatusCode)" } else { throw }
}
