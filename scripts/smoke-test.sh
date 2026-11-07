#!/usr/bin/env bash
# Einfacher Test gegen die laufende Seite: Ist sie erreichbar, ist das Zertifikat noch lange genug gültig,
# und sind Fotos, Dokumente und Benutzerverwaltung weiterhin nur nach der Anmeldung zugänglich?
# Es wird nichts verändert und niemand angemeldet, es gibt nur GET-Anfragen.
#
# Aufruf:  bash scripts/smoke-test.sh https://ffw-walting.de

set -u

BASIS="${1:-https://ffw-walting.de}"
BASIS="${BASIS%/}"
HOST="${BASIS#*://}"
HOST="${HOST%%/*}"
ZERO="00000000-0000-0000-0000-000000000000"

fehler=0

ok()   { printf '  ok      %s\n' "$1"; }
falsch() { printf '  FEHLER  %s\n' "$1"; echo "::error::$1"; fehler=$((fehler + 1)); }

# curl wiederholt bei kurzzeitigen Fehlern (Timeout, 5xx) selbst, damit ein kurzer Neustart keinen Alarm auslöst.
abfragen() {
  curl --silent --show-error --output /dev/null \
       --max-time 20 --retry 3 --retry-delay 10 \
       --write-out '%{http_code} %{redirect_url}' "$BASIS$1" 2>/dev/null
}

erwarte_status() {
  local pfad="$1" soll="$2" antwort status
  antwort="$(abfragen "$pfad")"
  status="${antwort%% *}"
  if [ "$status" = "$soll" ]; then ok "GET $pfad -> $status"; else falsch "GET $pfad liefert $status, erwartet $soll"; fi
}

erwarte_anmeldung() {
  local pfad="$1" antwort status ziel
  antwort="$(abfragen "$pfad")"
  status="${antwort%% *}"
  ziel="${antwort#* }"
  if [ "$status" = "302" ] && [[ "$ziel" == *"/anmelden"* ]]; then
    ok "GET $pfad -> Weiterleitung zur Anmeldung"
  else
    falsch "GET $pfad ist ohne Anmeldung zugänglich oder kaputt (Status $status, Ziel '${ziel}')"
  fi
}

echo "Öffentliche Seiten ($BASIS)"
erwarte_status "/" 200
erwarte_status "/anmelden" 200
erwarte_status "/kostenuhr" 200

echo "Geschützte Seiten und Dateien (ohne Anmeldung)"
erwarte_anmeldung "/fotos"
erwarte_anmeldung "/dokumente"
erwarte_anmeldung "/benutzer"
erwarte_anmeldung "/konto"
erwarte_anmeldung "/fotos/bild/$ZERO"
erwarte_anmeldung "/fotos/vorschau/$ZERO"
erwarte_anmeldung "/dokumente/datei/$ZERO"

echo "Anmeldeseite zeigt das Formular"
seite="$(curl --silent --max-time 20 --retry 3 --retry-delay 10 "$BASIS/anmelden" 2>/dev/null)"
if grep -q 'name="Input.Password"' <<<"$seite"; then ok "Passwortfeld vorhanden"; else falsch "Auf /anmelden fehlt das Passwortfeld"; fi

echo "TLS-Zertifikat"
if command -v openssl >/dev/null 2>&1 && [[ "$BASIS" == https://* ]]; then
  if echo | openssl s_client -servername "$HOST" -connect "$HOST:443" 2>/dev/null \
       | openssl x509 -noout -checkend $((14 * 24 * 3600)) >/dev/null 2>&1; then
    ok "Zertifikat ist noch mindestens 14 Tage gültig"
  else
    falsch "Das Zertifikat von $HOST läuft in weniger als 14 Tagen ab (oder ist nicht lesbar)"
  fi
else
  echo "  übersprungen"
fi

echo
if [ "$fehler" -eq 0 ]; then
  echo "Alles in Ordnung."
else
  echo "$fehler Prüfung(en) fehlgeschlagen."
  exit 1
fi
