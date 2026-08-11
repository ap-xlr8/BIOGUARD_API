# RUNBOOK de Produccion - BioGuard

Documento operativo para desplegar, monitorear y resolver incidentes del ecosistema BioGuard en produccion.

## Arquitectura

```
+------------------+      +----------------------+
|  React Web (SPA) | ---> |  .NET 10 API          |
|  bioguard-web    |      |  bioguard-api         |  DigitalOcean App Platform (nyc)
|  nginx + proxy   |      |  /api, /health        |
+------------------+      +----------+-----------+
                                     |
                      +--------------+--------------+
                      |                            |
              +-------v--------+           +--------v---------+
              |  MongoDB Atlas |           |  Stripe / PayPal |
              |  (TLS, TTL)    |           |  Firebase (FCM)  |
              +----------------+           +------------------+

+------------------+      +------------------+
|  Android Movil   |      |  WearOS          |   Google Play Store
|  com.example...  |      |  com.example...  |
+------------------+      +------------------+
```

| Componente | Repositorio | URL / App | Despliegue |
|---|---|---|---|
| Backend | `Backend-BioGuard` | `https://bioguard-api-lkvnq.ondigitalocean.app` | GHCR + App Platform (auto desde `master`) |
| Web | `Web-BioGuard` | `bioguard-web` (App Platform) | GHCR `frontendweb-bioguard` + App Platform |
| Movil | `BioGuardMovil` | Play Store (`com.example.bioguard_movil`) | AAB firmado a Play Console |
| Wearable | `Wearables-BioGuard` | Play Store (`com.example.bioguard_wearos`) | AAB firmado a Play Console |

## Monitoreo y Health Checks

### Backend

```bash
curl -s https://bioguard-api-lkvnq.ondigitalocean.app/health
# {"status":"healthy","timestamp":"..."}
```

- App Platform ejecuta el health check cada 30s (`/health`). Si falla 3 veces seguidas, reinicia el contenedor.
- Dashboard DO App Platform: metricas de CPU, memoria, latencia y logs.

### Web

- Health check en `/` (el proxy nginx responde 200 con `index.html`).
- El proxy expone el estado del backend en `/health` del propio web:
  ```bash
  curl -s https://<web-domain>/health
  ```

### MongoDB Atlas

- Atlas > Clusters > Metrics: CPU, memoria, conexiones, almacenamiento.
- Configurar alertas por: CPU > 70%, almacenamiento > 70%, fallo de autenticacion, backups fallidos.

## Despliegue por componente

### Backend (auto-deploy)

1. Push a `master` (PR merge) dispara `.github/workflows/ci.yml`:
   - Build + 532 tests + coverage.
   - Docker build multi-stage y push a `ghcr.io/ap-xlr8/bioguard_api`.
   - Deploy a App Platform usando `.do/app.yaml` (digest de la imagen nueva).
2. Verificar `/health` y un endpoint autenticado.

### Web (auto-deploy)

1. Push a `master` con cambios en `FrontendWebBioGuard/**` dispara `.github/workflows/deploy-web.yml`:
   - `npm ci && npm run build` (dentro de Docker).
   - Push a `ghcr.io/ap-xlr8/frontendweb-bioguard`.
   - Deploy a App Platform con `.do/app-web.yaml`.
2. El nginx del contenedor sirve el SPA y proxya `/api` y `/health` al backend de produccion.

### Movil / Wearable (manual)

1. Generar el AAB firmado:
   ```bash
   # Requiere $env:JAVA_HOME = "C:\Program Files\Android\Android Studio\jbr"
   ./gradlew bundleRelease
   ```
   El CI tambien genera el AAB con los secretos `BIOGUARD_MOVIL_*` / `BIOGUARD_WEAR_*`.
2. Subir a Play Console > Release > Production.
3. Publicar y monitorear tasas de crash en Play Console.

## Backups

- MongoDB Atlas: **P0 cluster tiene backups automaticos habilitados**. Verificar snapshot diario en Atlas > Backups.
- Restaurar: Atlas > Backups > Restore (a un cluster temporal primero, validar, luego a produccion).
- No hay datos locales criticos: SQLite del movil es cache offline (se resincroniza con `lectura-batch`).

## Troubleshooting

| Sintoma | Causa probable | Resolucion |
|---|---|---|
| `/health` responde 500 | `CRIPTO_KEY` o `JWT_SECRET_KEY` vacios | Verificar env vars en App Platform; `CriptoService` lanza `InvalidOperationException` si ambas estan vacias (`CriptoService.cs`) |
| 500 en login/auth | `JWT_SECRET_KEY` incorrecto tras rotacion | Rotar con la guia `ROTACION_SECRETOS.md`; reiniciar contenedor |
| 403 en `/api` desde la web | Edge de DO rechaza Host header incorrecto | El proxy nginx **no debe sobreescribir** `Host`; usar `proxy_pass` con variable + `resolver` |
| 401/403 en llamadas autenticadas | Token expirado o rol insuficiente | Re-login; verificar rol (`dueno`, `paciente`, `cuidador`, `admin`) |
| 429 en login/registro | Rate limiting (login 5/min, register 3/min) | Esperar; verificar si una IP esta abusando |
| 413 en batch | Limite de 10MB en `lectura-batch` | El movil debe particionar el lote |
| Timeout en `exportar-pdf` | Respuestas CSV grandes | Usar rangos de fechas pequenos |
| App no actualiza al publicar en Play | Keystore erroneo / Play App Signing | Verificar keystore; activar Play App Signing |
| Web muestra datos viejos | Cache del navegador de `index.html` | Los assets son inmutables (cache 1y); `index.html` no debe cachearse |
| CI falla en lint del frontend | Errores preexistentes de eslint | Corregir o ejecutar el job de build (no lint) |

## Rollback

### Backend / Web (App Platform)

1. DigitalOcean App Platform > app > Deployments.
2. Seleccionar el deployment anterior (digest de imagen previo) y elegir **Rollback**.
3. Alternativa manual: en GitHub Actions, re-ejecutar el workflow con el SHA anterior:
   ```bash
   # En el workflow se taggea ghcr.io/ap-xlr8/bioguard_api:${{ github.sha }}
   # Volver a desplegar el digest de ese SHA via App Platform.
   ```

### Movil / Wearable (Play Console)

1. Play Console > Release > Production > historial de releases.
2. **Reenviar el AAB anterior** (la version previa publicada) o usar "Rollout" con la version estable.
3. En caso de fallo grave: Play Console permite pausar el rollout o retirar la release.

## Incidentes

### Checklist de incidente

1. Confirmar alcance: `/health` backend, web, DB, pagos.
2. Revisar logs de App Platform (columna de la app afectada) y metricas.
3. Aplicar el fix mas rapido y seguro: rollback de imagen o correccion de env var.
4. Comunicar estado al equipo (canal de operaciones).
5. Documentar en el postmortem: cronologia, causa raiz, acciones, previsiones.

### Escenarios frecuentes

- **Base de datos caida**: verificar Atlas (estado del cluster). El backend no degrada gracilmente; los 500 son la señal.
- **Fuga de secreto**: rotar inmediatamente (ver `ROTACION_SECRETOS.md`), revocar tokens, revisar logs de acceso.
- **Deploy fallido**: los workflows usan `digest` inmutable; si el despliegue falla, App Platform conserva el deployment anterior (no corta servicio automaticamente en App Platform, la instancia anterior sigue sirviendo hasta que el nuevo este healthy).

## Credenciales de prueba (solo desarrollo/staging)

```
Email:    seed_639204600292413571@bioguard.test
Password: SeedTest@123!
Rol:      dueno
```

> Nunca usar datos de produccion en entornos de prueba.

## Enlaces utiles

- Backend README: `README.md` (endpoints, arquitectura, tests)
- Politica de seguridad: `SECURITY.md`
- Rotacion de secretos: `docs/ROTACION_SECRETOS.md`
- Documentacion API: `DOCUMENTACION_API.md`
- Swagger (solo desarrollo): `http://localhost:5000/swagger`
