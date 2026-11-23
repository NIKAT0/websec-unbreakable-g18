#!/bin/sh
# Maakt de OpenFGA-store "unbreakable" aan en schrijft het autorisatiemodel (model.json).
# Enkel nodig bij een nieuwe installatie (lege openfga-db). Zet daarna de twee id's
# die dit script toont in docker-compose.yml (OpenFga__StoreId en OpenFga__ModelId).
# OpenFGA heeft geen poort naar buiten, dus we praten ermee via het interne netwerk.
set -e
cd "$(dirname "$0")"

NET=websec-unbreakable-g18_app-openfga
fga() {
  docker run --rm --network "$NET" -v "$PWD:/w:ro" curlimages/curl:8.11.1 -s "$@"
}

STORE=$(fga -X POST http://openfga:8080/stores \
  -H "content-type: application/json" -d '{"name":"unbreakable"}' \
  | sed -E 's/.*"id":"([^"]+)".*/\1/')

MODEL=$(fga -X POST "http://openfga:8080/stores/$STORE/authorization-models" \
  -H "content-type: application/json" -d @/w/model.json \
  | sed -E 's/.*"authorization_model_id":"([^"]+)".*/\1/')

echo "OpenFga__StoreId: \"$STORE\""
echo "OpenFga__ModelId: \"$MODEL\""
