#!/usr/bin/env bash
# Simula la pasarela: envía una notificación firmada con HMAC-SHA256 a la API.
# Uso: ./scripts/send-notification.sh <orderId> [eventId] [amount] [paymentId] [eventType]
# Variables: API_URL (default http://localhost:5080), WEBHOOK_SECRET (default dev-only-secret-change-me)
set -euo pipefail

ORDER_ID="${1:?orderId requerido}"
EVENT_ID="${2:-evt_$(date +%s%N)}"
AMOUNT="${3:-45000.00}"
PAYMENT_ID="${4:-pay_demo_${ORDER_ID}}"
EVENT_TYPE="${5:-payment.succeeded}"
API_URL="${API_URL:-http://localhost:5080}"
SECRET="${WEBHOOK_SECRET:-dev-only-secret-change-me}"

BODY=$(printf '{"eventId":"%s","eventType":"%s","paymentId":"%s","orderId":%s,"amount":%s,"currency":"COP","occurredAt":"%s"}' \
  "$EVENT_ID" "$EVENT_TYPE" "$PAYMENT_ID" "$ORDER_ID" "$AMOUNT" "$(date -u +%Y-%m-%dT%H:%M:%SZ)")
TS=$(date +%s)
SIG=$(printf '%s.%s' "$TS" "$BODY" | openssl dgst -sha256 -hmac "$SECRET" -hex | sed 's/^.*= //')

echo ">> $BODY"
curl -sS -X POST "$API_URL/api/payments/confirm-payment" \
  -H "Content-Type: application/json" \
  -H "X-Gateway-Signature: t=$TS,v1=$SIG" \
  -d "$BODY" -w "\n<< HTTP %{http_code}\n"
