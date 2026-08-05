# Rotacion de Secretos - BioGuard

Guia operativa para rotar secretos del ecosistema BioGuard (backend, web, movil y wearable) sin interrumpir el servicio.

## Inventario de secretos

| # | Secreto | Donde se usa | Quien lo genera | Frecuencia recomendada |
|---|---|---|---|---|
| 1 | `JWT_SECRET_KEY` | Backend (firma de tokens) | Equipo | Anual o ante sospecha de fuga |
| 2 | `MONGODB_CONNECTION_STRING` | Backend (conexion a Atlas) | MongoDB Atlas | Anual (o al rotar usuario DB) |
| 3 | `STRIPE_SECRET_KEY` / `STRIPE_WEBHOOK_SECRET` | Backend (pagos) | Stripe | Anual o ante sospecha |
| 4 | `PAYPAL_CLIENT_ID` / `PAYPAL_CLIENT_SECRET` | Backend (pagos PayPal) | PayPal | Anual o ante sospecha |
| 5 | `FIREBASE_SERVER_KEY` | Backend (notificaciones push) | Firebase Console | Anual o ante sospecha |
| 6 | `GOOGLE_CLIENT_ID` (+ secret) | Backend + Web (OAuth Google) | Google Cloud Console | Anual o ante sospecha |
| 7 | SMTP (`User`/`Pass`) | Backend (envio de correos) | Proveedor SMTP | Anual |
| 8 | `CRIPTO_KEY` | Backend (cifrado de datos) | Equipo | Anual (fallback: deriva de `JWT_SECRET_KEY`) |
| 9 | `DIGITALOCEAN_ACCESS_TOKEN` | GitHub Actions (deploy) | DigitalOcean | Cada 90 dias o ante sospecha |
| 10 | Keystore MOVIL (`BIOGUARD_MOVIL_*`) | Firma del APK/AAB | Equipo (jks) | **No se rota**; se respalda |
| 11 | Keystore WEARABLE (`BIOGUARD_WEAR_*`) | Firma del APK/AAB | Equipo (jks) | **No se rota**; se respalda |
| 12 | GitHub Secrets (repo) | CI/CD | Equipo | Cuando se rote el valor subyacente |

> **Keystores de Android NO se rotan.** La Play Store identifica la app por el keystore de firma.
> Si se pierde o cambia, no se pueden publicar actualizaciones de la app existente.
> Se debe generar **una sola vez** y guardar en un gestor de contrasenas (copia del `.jks` + passwords).

## Inventario por entorno

| Variable | Produccion | Staging |
|---|---|---|
| Connection string | `MONGODB_CONNECTION_STRING` | `STAGING_MONGODB_CONNECTION_STRING` |
| JWT | `JWT_SECRET_KEY` | `STAGING_JWT_SECRET_KEY` |
| Google OAuth | `GOOGLE_CLIENT_ID` | `STAGING_GOOGLE_CLIENT_ID` |
| Stripe | `STRIPE_SECRET_KEY` (+`STRIPE_WEBHOOK_SECRET`) | `STAGING_STRIPE_SECRET_KEY` |
| PayPal | `PAYPAL_CLIENT_ID` | `STAGING_PAYPAL_CLIENT_ID` |
| Firebase | `FIREBASE_SERVER_KEY` | `STAGING_FIREBASE_SERVER_KEY` |
| AllowedHosts | `ALLOWED_HOSTS` | `STAGING_ALLOWED_HOSTS` |

Cada secreto existe en **dos lugares**: el secreto de GitHub (usado por los workflows) y la variable de entorno de DigitalOcean App Platform (usada por el deploy). Ambos deben rotarse.

## Generacion de valores seguros

```bash
# Clave JWT / secreto simetrico (min 32 chars)
openssl rand -base64 48

# Password para keystore o SMTP
openssl rand -base64 24

# Verificar entropia de una clave existente
printf "%s" "TU_CLAVE" | wc -c
```

## Procedimientos

### 1. Rotar `JWT_SECRET_KEY`

Impacto: invalida **todas** las sesiones activas (usuarios deben volver a iniciar sesion). Planificar en ventana de baja actividad.

1. Generar nuevo valor: `openssl rand -base64 48`.
2. En DigitalOcean App Platform (app `bioguard-api`): editar la env var `JWT_SECRET_KEY` y guardar. App Platform redeploya con el nuevo valor.
3. En GitHub > Settings > Secrets: actualizar el secreto `JWT_SECRET_KEY` con el mismo valor.
4. Repetir para staging (`STAGING_JWT_SECRET_KEY`) si aplica.
5. Verificar:
   - `curl -s https://bioguard-api-lkvnq.ondigitalocean.app/health` → `healthy`.
   - Login de prueba con un usuario real → emite token valido.
   - Las sesiones previas quedan invalidadas (401 con `token invalido`).

> Nota: el backend no soporta doble clave (`JWT:Key` anterior + nueva). Si se quiere rotar sin derribar sesiones, primero se despliega una version con soporte de claves multiples y luego se rota.

### 2. Rotar `MONGODB_CONNECTION_STRING`

1. En MongoDB Atlas crear un **nuevo usuario de base de datos** con los roles minimos del cluster (`readWrite` en `bioguard`, sin permisos de admin).
2. Copiar la nueva connection string (clúster > Connect > Drivers).
3. Actualizar la env var en App Platform (prod y staging) y el secreto de GitHub.
4. Esperar redeploy y validar:
   - `/health` → `healthy`.
   - `GET /api/Planes` responde datos (confirma lectura).
   - Registrar un usuario de prueba (confirma escritura).
5. Una vez confirmado y observado por 24-48h, **eliminar el usuario anterior** de Atlas.

### 3. Rotar claves de pago (Stripe / PayPal)

1. Stripe Dashboard > Developers > API keys: crear nueva key secreta.
2. Stripe Dashboard > Webhooks: crear/renovar endpoint y obtener el nuevo `STRIPE_WEBHOOK_SECRET` firmando con la nueva key.
3. Actualizar env vars en App Platform + secretos de GitHub.
4. Probar una sesion de pago en un entorno controlado (Stripe en modo test primero, luego prod).
5. Revocar la key anterior en el dashboard del proveedor tras 24-48h de operacion OK.

### 4. Rotar `FIREBASE_SERVER_KEY`

1. Firebase Console > Project settings > Cloud Messaging: generar nueva Server Key.
2. Actualizar env var en App Platform + secreto de GitHub.
3. Enviar una notificacion de prueba (endpoint `POST /api/Notificaciones` o trigger de alerta).
4. Revocar la key anterior en Firebase.

### 5. Rotar `GOOGLE_CLIENT_ID` (OAuth)

1. Google Cloud Console > APIs & Services > Credentials: crear nuevo OAuth Client (Web).
2. Actualizar en el backend (env `GOOGLE_CLIENT_ID`) y, si la web usa login Google, en las variables `VITE_*` de la web antes de buildear.
3. Agregar los orígenes/redirecciones permitidos (dominio web de produccion) en la configuracion del cliente.
4. Actualizar secretos de GitHub y redeployar backend y web.
5. Probar login con Google.
6. Borrar el cliente antiguo pasada una semana.

### 6. Rotar SMTP

1. Generar nuevo password/app-password del proveedor (ej. SendGrid API key).
2. Actualizar `SMTP_USER`/`SMTP_PASS` en App Platform (backend) + GitHub.
3. Enviar un correo de prueba (registro o forgot-password).
4. Revocar la key anterior en el proveedor.

### 7. Rotar `DIGITALOCEAN_ACCESS_TOKEN`

1. DigitalOcean > API > Tokens: crear nuevo token con scope de App Platform.
2. GitHub > Settings > Secrets > Actions: reemplazar `DIGITALOCEAN_ACCESS_TOKEN` (entorno `production` y el que corresponda a staging).
3. Ejecutar un deploy manual (`workflow_dispatch`) de backend o web para validar.
4. Revocar el token anterior.

### 8. Respaldo y custodia de keystores Android

Los keystores no se rotan, pero **si se pierden** hay que recuperar la clave de firma desde Play Console (Play App Signing) si estaba activada. Pasos:

1. Generar keystore (una sola vez):
   ```bash
   keytool -genkeypair -v \
     -keystore bioguard-movil-release.jks \
     -keyalg RSA -keysize 2048 -validity 10000 \
     -alias bioguard-movil
   ```
2. Guardar en gestor de contrasenas: el `.jks`, el alias y las 2 passwords.
3. Subir el AAB firmado a Play Console y **activar Play App Signing** (así Google guarda la clave de firma y permite recuperarla).
4. En GitHub agregar los secretos:
   - `BIOGUARD_MOVIL_STORE_FILE` (contenido base64 del `.jks`), `BIOGUARD_MOVIL_STORE_PASSWORD`, `BIOGUARD_MOVIL_KEY_ALIAS`, `BIOGUARD_MOVIL_KEY_PASSWORD`.
   - Igual para wearable con prefijo `BIOGUARD_WEAR_*`.
5. La clave de upload (la nuestra) se puede regenerar en Play Console si se pierde, pero la de firma de la app **nunca debe perderse**.

## Orden de rotacion segura (todas las claves)

1. Generar el nuevo valor **antes** de tocar el viejo.
2. Actualizar primero el secreto de **GitHub** (para CI), luego la env var de **App Platform**.
3. Redeployar y validar con la checklist de abajo.
4. Mantener el valor anterior 24-48h por si hay que revertir.
5. Recién entonces revocar/destruir el valor anterior en el proveedor.

## Checklist post-rotacion

- [ ] `/health` responde `{"status":"healthy"}`
- [ ] `GET /api/Planes` responde 200 con datos
- [ ] Login web funciona (emite JWT + refresh token)
- [ ] Login Google funciona (si aplica)
- [ ] Registro + verificacion por email funciona (SMTP)
- [ ] Pago de prueba (Stripe/PayPal) completo
- [ ] Notificacion push de prueba llega al dispositivo
- [ ] CI (GitHub Actions) pasa con el nuevo secreto
- [ ] Ningun valor quedó hardcodeado ni en `.env`, `appsettings.json` ni en logs
- [ ] El valor anterior fue revocado en el proveedor
