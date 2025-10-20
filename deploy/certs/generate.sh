#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR=$(dirname "$0")
CA_DIR="$ROOT_DIR/ca"
SERVERS_DIR="$ROOT_DIR/servers"
CLIENTS_DIR="$ROOT_DIR/clients"

mkdir -p "$CA_DIR" "$SERVERS_DIR" "$CLIENTS_DIR"

CA_KEY="$CA_DIR/ca.key.pem"
CA_CERT="$CA_DIR/ca.crt.pem"

if [[ ! -f "$CA_CERT" ]]; then
  echo "Generating certificate authority..."
  openssl genrsa -out "$CA_KEY" 4096 >/dev/null 2>&1
  openssl req -x509 -new -nodes -key "$CA_KEY" \
    -sha256 -days 3650 -out "$CA_CERT" \
    -subj "/CN=BenchRunner Dev CA" >/dev/null 2>&1
else
  echo "Using existing CA at $CA_CERT"
fi

function generate_server_cert() {
  local name="$1"
  local dir="$SERVERS_DIR/$name"
  mkdir -p "$dir"

  local key="$dir/$name.key.pem"
  local csr="$dir/$name.csr.pem"
  local cert="$dir/$name.crt.pem"
  local pfx="$dir/$name.pfx"
  local config="$dir/openssl.cnf"

  cat > "$config" <<CONFIG
[ req ]
default_bits       = 2048
prompt             = no
default_md         = sha256
distinguished_name = dn
req_extensions     = req_ext

[ dn ]
CN = ${name}

[ req_ext ]
subjectAltName = @alt_names

[ alt_names ]
DNS.1 = ${name}
CONFIG

  openssl req -new -nodes -newkey rsa:2048 -keyout "$key" -out "$csr" -config "$config" >/dev/null 2>&1
  openssl x509 -req -in "$csr" -CA "$CA_CERT" -CAkey "$CA_KEY" -CAserial "$CA_DIR/ca.srl" -CAcreateserial \
    -out "$cert" -days 825 -sha256 -extensions req_ext -extfile "$config" >/dev/null 2>&1
  openssl pkcs12 -export -out "$pfx" -inkey "$key" -in "$cert" -password pass: >/dev/null 2>&1

  cp "$CA_CERT" "$dir/ca.crt.pem"
  rm -f "$csr" "$config"
}

function generate_client_cert() {
  local name="$1"
  local dir="$CLIENTS_DIR/$name"
  mkdir -p "$dir"

  local key="$dir/$name.key.pem"
  local csr="$dir/$name.csr.pem"
  local cert="$dir/$name.crt.pem"
  local pfx="$dir/$name.pfx"

  openssl req -new -nodes -newkey rsa:2048 -keyout "$key" -out "$csr" -subj "/CN=${name}" >/dev/null 2>&1
  openssl x509 -req -in "$csr" -CA "$CA_CERT" -CAkey "$CA_KEY" -CAserial "$CA_DIR/ca.srl" -CAcreateserial \
    -out "$cert" -days 825 -sha256 >/dev/null 2>&1
  openssl pkcs12 -export -out "$pfx" -inkey "$key" -in "$cert" -password pass: >/dev/null 2>&1

  cp "$CA_CERT" "$dir/ca.crt.pem"
  rm -f "$csr"
}

echo "Generating server certificates..."
for name in authserver gateway orderservice inventoryservice resultsservice; do
  generate_server_cert "$name"
  chmod 600 "$SERVERS_DIR/$name"/*.key.pem "$SERVERS_DIR/$name"/*.pfx
  chmod 644 "$SERVERS_DIR/$name"/*.crt.pem
  echo "  - $name"
done

echo "Generating client certificates..."
for name in results-service bench-runner; do
  generate_client_cert "$name"
  chmod 600 "$CLIENTS_DIR/$name"/*.key.pem "$CLIENTS_DIR/$name"/*.pfx
  chmod 644 "$CLIENTS_DIR/$name"/*.crt.pem
  echo "  - $name"
done

chmod 600 "$CA_KEY"
chmod 644 "$CA_CERT"

echo "Certificates generated under $ROOT_DIR"
