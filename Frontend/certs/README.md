This directory is mounted into the frontend container as `/etc/nginx/ssl`.

Required files:
- `fullchain.pem`
- `privkey.pem`

For local/testing usage you can generate a self-signed certificate:

```bash
mkdir -p certs
openssl req -x509 -nodes -newkey rsa:2048 -sha256 -days 365 \
  -keyout certs/privkey.pem \
  -out certs/fullchain.pem \
  -subj "/CN=localhost" \
  -addext "subjectAltName=DNS:localhost,IP:127.0.0.1"
```

Production:
- Replace files with your real certificate and private key.
- Keep `privkey.pem` access restricted.
