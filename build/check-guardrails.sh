#!/usr/bin/env bash
# Mechanical checks for the money and pricing rules in CLAUDE.md.
# Exit 2 sends the findings back to Claude Code when run as a hook.
set -u
cd "$(dirname "$0")/.." || exit 1

svc=src/Pizza.OrderService
dirs=()
for d in "$svc" tests/Pizza.OrderService.Tests; do [ -d "$d" ] && dirs+=("$d"); done
[ ${#dirs[@]} -eq 0 ] && exit 0

findings=""
add() { [ -n "$2" ] && findings+="$1"$'\n'"$2"$'\n'; }

add "double/float used (money must be decimal):" \
  "$(grep -rnwE --include='*.cs' --exclude-dir=bin --exclude-dir=obj 'double|float' "${dirs[@]}")"

add "Rounding outside Money.RoundCents:" \
  "$(grep -rnE --include='*.cs' --exclude-dir=bin --exclude-dir=obj '\b(Math|decimal|Decimal)\.(Round|Floor|Ceiling|Truncate)\b' "${dirs[@]}" \
     | grep -v "$svc/Pricing/Money.cs")"

[ -d "$svc/Pricing" ] && add "Pricing/ must not reference ASP.NET:" \
  "$(grep -rnE --include='*.cs' 'Microsoft\.AspNetCore' "$svc/Pricing")"

if [ -n "$findings" ]; then
  printf 'Guardrail check failed:\n%s' "$findings" >&2
  exit 2
fi
exit 0
