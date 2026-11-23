#!/bin/sh
# Maakt de OpenFGA-store "unbreakable" aan en schrijft het autorisatiemodel (model.json).
# Enkel nodig bij een nieuwe installatie (lege openfga-db). Zet daarna de twee id's
# die dit script toont in docker-compose.yml (OpenFga__StoreId en OpenFga__ModelId).
# OpenFGA heeft geen poort naar buiten, dus we praten ermee via het interne netwerk.
set -e
cd "$(dirname "$0")"

NET=websec-unbreakable-g18_app-openfga
# OpenFGA vraagt de gedeelde sleutel (OPENFGA_KEY uit ../.env). Via een bestand, niet op de opdrachtregel.
KEY=$(grep '^OPENFGA_KEY=' ../.env | cut -d= -f2-)
[ -n "$KEY" ] || { echo "OPENFGA_KEY ontbreekt in .env"; exit 1; }
HDR=$(mktemp); trap 'rm -f "$HDR"' EXIT
printf 'Authorization: Bearer %s\n' "$KEY" > "$HDR"
fga() {
  # -u 0: het sleutelbestand is enkel leesbaar voor root
  docker run --rm -u 0 --network "$NET" -v "$PWD:/w:ro" -v "$HDR:/h:ro" curlimages/curl:8.11.1 -s -H @/h "$@"
}

STORE=$(fga -X POST http://openfga:8080/stores \
  -H "content-type: application/json" -d '{"name":"unbreakable"}' \
  | sed -E 's/.*"id":"([^"]+)".*/\1/')

MODEL=$(fga -X POST "http://openfga:8080/stores/$STORE/authorization-models" \
  -H "content-type: application/json" -d @/w/model.json \
  | sed -E 's/.*"authorization_model_id":"([^"]+)".*/\1/')

echo "OpenFga__StoreId: \"$STORE\""
echo "OpenFga__ModelId: \"$MODEL\""
