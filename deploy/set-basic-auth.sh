#!/bin/bash
# Cloud krok 5: nastaví heslo pro Caddy (basic auth) na VM. Spouštět v ~/minishop:
#   bash set-basic-auth.sh
# Heslo se zadá skrytě, do .env se uloží jen jeho bcrypt otisk (BASIC_AUTH_HASH). Pak restartuje Caddy.
set -euo pipefail
cd "$(dirname "$0")"

read -rsp "Nové heslo do MiniShopu: " password; echo
read -rsp "Heslo znovu: " confirm; echo
if [ -z "$password" ] || [ "$password" != "$confirm" ]; then
    echo "Hesla jsou prázdná nebo se neshodují – nic se nezměnilo." >&2
    exit 1
fi

# caddy hash-password čte heslo a jeho potvrzení ze vstupu (dva řádky).
hash=$(printf '%s\n%s\n' "$password" "$password" | docker run --rm -i caddy:2 caddy hash-password)
unset password confirm

# Otisk do .env v jednoduchých uvozovkách (obsahuje $). Bcrypt používá jen [./$A-Za-z0-9], oddělovač | je bezpečný.
if grep -q '^BASIC_AUTH_HASH=' .env; then
    sed -i "s|^BASIC_AUTH_HASH=.*|BASIC_AUTH_HASH='$hash'|" .env
else
    echo "BASIC_AUTH_HASH='$hash'" >> .env
fi

docker compose up -d
echo "Hotovo. Uživatel: $(grep '^BASIC_AUTH_USER=' .env | cut -d= -f2)"
