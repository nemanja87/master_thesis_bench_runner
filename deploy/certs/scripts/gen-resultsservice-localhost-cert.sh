#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR=$(cd -- "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
CA_DIR="$ROOT_DIR/ca"
SERVER_DIR="$ROOT_DIR/servers/resultsservice"

CA_CERT="$CA_DIR/ca.crt.pem"
CA_KEY="$CA_DIR/ca.key.pem"

if [[ ! -f "$CA_CERT" || ! -f "$CA_KEY" ]]; then
  echo "CA certificate/key not found in $CA_DIR" >&2
  exit 1
fi

mkdir -p "$SERVER_DIR"

TMP_DIR=$(mktemp -d)
trap 'rm -rf "$TMP_DIR"' EXIT

OPENSSL_CNF="$TMP_DIR/openssl.cnf"
cat > "$OPENSSL_CNF" <<CFG
[ req ]
default_bits       = 2048
prompt             = no
default_md         = sha256
distinguished_name = dn
req_extensions     = req_ext

[ dn ]
CN = resultsservice

[ req_ext ]
subjectAltName = @alt_names

[ alt_names ]
DNS.1 = resultsservice
DNS.2 = localhost
CFG

CERT_CSR="$TMP_DIR/resultsservice.csr.pem"
CERT_KEY="$SERVER_DIR/resultsservice.key.pem"
CERT_PEM="$SERVER_DIR/resultsservice.crt.pem"
CERT_PFX="$SERVER_DIR/resultsservice.pfx"

openssl req -new -nodes -newkey rsa:2048 -keyout "$CERT_KEY" -out "$CERT_CSR" -config "$OPENSSL_CNF"
openssl x509 -req -in "$CERT_CSR" -CA "$CA_CERT" -CAkey "$CA_KEY" -CAcreateserial -out "$CERT_PEM" -days 825 -sha256 -extensions req_ext -extfile "$OPENSSL_CNF"
rm -f "$CERT_PFX"
openssl pkcs12 -export -out "$CERT_PFX" -inkey "$CERT_KEY" -in "$CERT_PEM" -password pass:

chmod 600 "$CERT_KEY" "$CERT_PFX"
chmod 644 "$CERT_PEM"

echo "Generated ResultsService certificate with SAN=resultsservice,localhost"
