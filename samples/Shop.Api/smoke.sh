#!/usr/bin/env bash
# Run against a fresh Shop.Api host, either JIT or Native AOT. Requires curl and jq.
set -euo pipefail

api="${1:-http://127.0.0.1:5080}"
api="${api%/}"
body=''

request() {
  local method="$1" path="$2" expected="$3" response status
  local args=(--silent --show-error --max-time 15 --request "$method" --write-out $'\n%{http_code}')
  if [[ $# -eq 4 ]]; then
    args+=(--header 'Content-Type: application/json' --data "$4")
  fi
  if ! response=$(curl "${args[@]}" "$api$path"); then
    printf 'FAIL %s %s: request failed\n%s\n' "$method" "$path" "$response" >&2
    exit 1
  fi
  status="${response##*$'\n'}"
  body="${response%$'\n'*}"
  if [[ "$status" != "$expected" ]]; then
    printf 'FAIL %s %s: expected HTTP %s, got %s\n%s\n' "$method" "$path" "$expected" "$status" "$body" >&2
    exit 1
  fi
  printf 'PASS %s %s: HTTP %s\n' "$method" "$path" "$status"
}

expect() {
  if ! jq --exit-status "$1" <<< "$body" > /dev/null; then
    printf 'FAIL JSON assertion: %s\nReceived: %s\n' "$1" "$body" >&2
    exit 1
  fi
}

request GET /orders/aot-missing 404
request POST /stock/KEYBOARD 202 '{"quantity":5}'

# The original one-line command and response fields remain available.
request POST /orders 202 '{"orderId":"aot-legacy","sku":"KEYBOARD","quantity":1,"amount":89}'
request GET /orders/aot-legacy 200
expect '.order == {orderId:"aot-legacy",sku:"KEYBOARD",quantity:1,amount:89,status:"Paid"} and .fulfillment == "Awaiting"'
request POST /webhooks/payments 202 '{"orderId":"aot-legacy"}'
request GET /orders/aot-legacy 200
expect '.order.status == "Shipped" and .fulfillment == "Completed" and .details.total == {amount:89,currency:"EUR"}'
request POST /orders 409 '{"orderId":"aot-legacy","sku":"KEYBOARD","quantity":1,"amount":89}'
expect '.title == "AlreadyPlaced"'

# Amount is the whole line total. Omitted currency uses EUR.
request POST /orders/aot-draft/lines 202 '{"lineId":"keep","sku":"KEYBOARD","quantity":2,"amount":100}'
request POST /orders/aot-draft/lines 202 '{"lineId":"remove","sku":"KEYBOARD","quantity":1,"amount":40,"currency":"EUR"}'
request GET /orders/aot-draft 200
expect '.order == null and .fulfillment == null and .details.status == "Draft" and (.details.lines | length) == 2 and .details.total == {amount:140,currency:"EUR"}'
request DELETE /orders/aot-draft/lines/remove 202
request GET /orders/aot-draft 200
expect '.details.total.amount == 100 and any(.details.lines[]; .lineId == "remove" and .isCancelled)'
request POST /orders/aot-draft/lines 409 '{"lineId":"mixed","sku":"KEYBOARD","quantity":1,"amount":10,"currency":"USD"}'
expect '.title == "CurrencyMismatch"'
request POST /orders/aot-draft/submit 202
request GET /orders/aot-draft 200
expect '.order.quantity == 2 and .order.amount == 100 and .order.status == "Paid" and .fulfillment == "Awaiting"'
request DELETE /orders/aot-draft/lines/keep 409
expect '.title == "AlreadyPlaced"'
request POST /orders/aot-draft/cancel 409
expect '.title == "AlreadyPlaced"'
request POST /webhooks/payments 202 '{"orderId":"aot-draft"}'
request GET /orders/aot-draft 200
expect '.order.status == "Shipped" and .fulfillment == "Completed" and .details.total.amount == 100'

# Cancelling an editable draft never starts fulfillment or reserves stock.
request POST /orders/aot-cancel/lines 202 '{"lineId":"one","sku":"KEYBOARD","quantity":1,"amount":15}'
request POST /orders/aot-cancel/cancel 202
request GET /orders/aot-cancel 200
expect '.order == null and .fulfillment == null and .details.status == "Cancelled" and .details.total.amount == 15'
request GET /stock/KEYBOARD 200
expect '.sku == "KEYBOARD" and .available == 2'
request GET /orders 200
expect 'length == 2 and all(.[]; .status == "Shipped")'
request GET /order-details 200
expect 'length == 3'

# Concrete integration-event metadata comes from ShopJson, including DateOnly.
request GET /dev/published 200
expect 'any(.[]; .orderId == "aot-legacy" and .amount == 89 and .currency == "EUR") and any(.[]; .orderId == "aot-draft" and .amount == 100 and .currency == "EUR") and any(.[]; .orderId == "aot-legacy" and (.shippedOn | type) == "string") and any(.[]; .orderId == "aot-draft" and (.shippedOn | type) == "string")'
request GET /dev/journal 200
expect 'length > 0 and all(.[]; type == "string")'
request POST '/dev/clock/advance?minutes=0' 200
expect '.now | type == "string"'

printf 'Shop API HTTP smoke passed.\n'
