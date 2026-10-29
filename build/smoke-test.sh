#!/usr/bin/env bash
# Checks a running `docker compose up` stack end to end, through the builder's nginx proxy.
# Usage: build/smoke-test.sh [base-url]   (default http://localhost:8080)
set -u
base="${1:-http://localhost:8080}"
failures=0

check() { # name, expected, actual
  if [ "$2" = "$3" ]; then echo "ok   $1"; else echo "FAIL $1: expected '$2', got '$3'"; failures=$((failures + 1)); fi
}
status() { curl -s -o /dev/null -w '%{http_code}' "$@"; }
field() { grep -o "\"$1\":\"*[^,\"}]*" | head -1 | sed -E "s/\"$1\":\"*//"; }
post() { # path, key, body
  curl -s -X POST "$base$1" -H 'Content-Type: application/json' -H "Idempotency-Key: $2" -d "$3"
}

for _ in $(seq 1 30); do [ "$(status "$base/api/menu")" = 200 ] && break; sleep 1; done

check "builder page" 200 "$(status "$base/")"
check "unknown path falls back to the builder" 200 "$(status "$base/orders/anything")"
check "menu" 200 "$(status "$base/api/menu")"

g2='{"lineItems":[{"size":"large","quantity":2,"toppings":["bacon","pineapple"]}],"promoCode":"ILOVEPIZZA","tip":{"mode":"percent","value":15}}'
check "G2 quote total" 32.27 "$(curl -s -X POST "$base/api/quotes" -H 'Content-Type: application/json' -d "$g2" | field total)"

key="smoke-$(date +%s)-$RANDOM"
body='{"lineItems":[{"size":"small","quantity":1,"toppings":[]}],"expectedTotal":5.33}'
first=$(post /api/orders "$key" "$body" | field id)
check "submit creates an order" yes "$([ -n "$first" ] && echo yes)"
check "resubmitting with one key returns the same order" "$first" "$(post /api/orders "$key" "$body" | field id)"

# Bodyless POSTs with no Content-Length, as curl and other non-browser clients send them.
check "advance" Started "$(curl -s -X POST "$base/api/orders/$first/advance" | field status)"
other=$(post /api/orders "$key-2" "$body" | field id)
check "cancel" Cancelled "$(curl -s -X POST "$base/api/orders/$other/cancel" -H "Idempotency-Key: $key-3" | field status)"

[ "$failures" -eq 0 ] && echo "All checks passed." || echo "$failures check(s) failed."
exit $((failures > 0))
