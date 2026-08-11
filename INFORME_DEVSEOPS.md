# Informe DevSecOps — BIOGUARD_API (Backend .NET)

**Fecha:** 2026-08-10
**Repo:** `ap-xlr8/BIOGUARD_API` · **Rama:** `main`
**Stack:** ASP.NET Core (net10.0), MongoDB Atlas, GHCR + DigitalOcean App Platform

---

## 1. Exposición de información sensible

**Veredicto: NO se encontraron credenciales reales** en archivos versionados ni en el historial git completo (`git rev-list --all` + grep sobre todos los blobs).

| Elemento | Estado |
|---|---|
| `appsettings.json` | OK — todos los valores sensibles vacíos (`Jwt:Key`, `Stripe:*`, `PayPal:*`, `Firebase:*`, `ImgBB:ApiKey`, `Smtp:Password`) |
| `appsettings.Example.json`, `env.example` | OK — placeholders `<usuario>:<password>`, `<tu-clave-secreta…>` |
| `.gitignore` | OK — ignora `appsettings.Development.json`, `appsettings.*.Local.json`, `*.env`, `.env.*` (re-incluye las plantillas) |
| `.dockerignore` | OK — excluye `**/.env`, `**/.git`, `env.example`, `secrets.dev.yaml` |
| Historial git | OK — sin secretos (solo `sk_test_mock` en test factory) |
| Tests | OK — mocks de test (`sk_test_mock`, JWT de test, credenciales de prueba) |

### Hallazgos menores (baja severidad)
- `Program.cs:458` — fallback hardcodeado `"dev-seed-secret"` (solo Dev + `Seed:Enabled`); `Program.cs:320` — `SeedTest@123!` en seed.
- `DOCUMENTACION_API.md` / `DOCUMENTACION_JSON_ENDPOINTS.md` — tokens JWT de ejemplo (truncados).
- `k6/smoke-test.js:15` — `TEST_PASSWORD || 'Test@123!'` por defecto.
- `test_api_endpoints.py:72-74` — credenciales de prueba hardcodeadas contra URL de Render.

> Los secretos reales viven solo en GitHub Secrets + App Platform (confirmado en `docs/ROTACION_SECRETOS.md`).

---

## 2. CI/CD — DevSecOps

### `.github/workflows/ci.yml` (BioGuard CI/CD)
- Trigger: push/PR a `[master, main]`; `concurrency`; `permissions: {}`.
- **Seguridad incluida:** NuGet Audit (`dotnet list package --vulnerable --include-transitive`, falla build), hadolint, chequeo de licencias copyleft, cobertura 70%, SBOM anchore, firma **cosign keyless**, tags `sha-<sha>`.
- Deploy gateado por **rama + environment** (staging/production).

### `.github/workflows/security.yml` (DevSecOps)
- **CodeQL** (C#), **Trivy** (container, fail CRITICAL/HIGH), **Gitleaks** (fetch-depth 0), programado semanal.

### `.github/workflows/dast.yml` (OWASP ZAP)
- Baseline + API Scan (OpenAPI).
- **⚠️ Bug:** trigger `workflow_run` filtra `branches: [master]` pero la rama real es `main` → **nunca dispara**. Además el target por defecto es **producción**.

### `.github/workflows/deploy-manual.yml`
- Deploy manual por `workflow_dispatch` con `image_digest`; secrets desde GitHub Secrets. Correcto.

### Dependabot (`.github/dependabot.yml`)
- Cobertura: nuget (BioGuard.Api, Test1BioGuard), github-actions, docker — semanal. OK.

---

## 3. Dockerfile

- Base `mcr.microsoft.com/dotnet/aspnet:10.0` / `sdk:10.0` — **tag flotante sin pin por digest** (hadolint lo deja como warning DL3007).
- Multi-stage ✓ · `USER $APP_UID` (no-root) ✓ · sin secretos/.env en imagen ✓.
- **Sin `HEALTHCHECK`** en Dockerfile (solo en compose/DO spec).
- Dos Dockerfiles idénticos (raíz y `BioGuard.Api/`).

---

## 4. Seguridad de la aplicación

**Bien implementado:**
- JWT: clave desde config/env, mínimo 32 bytes verificado, HS256, validación completa, expiración 30 min; refresh tokens con **rotación atómica + detección de reuso**; blacklist por `jti`.
- Password: **PBKDF2-SHA256 600k iteraciones**, salt 16B, `FixedTimeEquals`; lockout 5 intentos/15 min; 2FA por email.
- **Rate limiting** (AspNetCoreRateLimit + Redis obligatorio en prod).
- CORS restringido a orígenes explícitos; HSTS + redirección HTTPS; `ForwardedHeaders` con `KnownProxies`; sin header `Server`.
- Manejo de errores global sin fuga de stack; emails enmascarados en logs.
- Stripe: firma de webhook verificada + idempotencia por `event.id`.
- Mongo: TLS 1.2/1.3 forzado; credenciales solo por config/env.
- Swagger solo en Development.

**A mejorar:**
- `AllowedHosts: "*"` en `appsettings.json` (cerrar por env en prod).
- Refresh tokens **en claro (base64)** en MongoDB → considerar hash.
- `AuthController.cs:114` / `AuditoriaService.cs` registran el **email completo** en `login_fallido`.
- PayPal: stub fail-closed (funciona, pero inoperativo).
- `UseHsts` debería limitarse a HTTPS/prod.

---

## 5. Dependencias

| Paquete | Versión | Observación |
|---|---|---|
| Microsoft.AspNetCore.Authentication.JwtBearer | **9.0.18** | ⚠️ Desalineado: app en net10.0 → usar 10.0.x |
| Microsoft.AspNetCore.OpenApi | **9.0.18** | ⚠️ Ídem |
| Microsoft.AspNetCore.Mvc.Testing | **9.0.18** | ⚠️ Ídem (test) |
| Swashbuckle.AspNetCore | **6.9.0** | ⚠️ Desactualizado (existe 7.x) |
| AspNetCoreRateLimit | 5.0.0 | Mantenimiento bajo — valorar sustitución |
| xunit 2.9.3 + runner 3.1.5 | — | Mezcla runner v3 / core v2 |
| MongoDB.Driver 3.10.0, Stripe.net 47.4.0, FirebaseAdmin 3.6.0 | — | OK |

> El audit de vulnerabilidades del CI solo cubre `BioGuard.Api.csproj`, no el proyecto de tests.

---

## 6. Priorización

| # | Severidad | Acción |
|---|---|---|
| 1 | Media | Actualizar paquetes ASP.NET 9.0.18 → 10.x y Swashbuckle 6.9 → 7.x |
| 2 | Media | Arreglar `dast.yml`: filtro `branches: [main]` y target **staging** (no producción) |
| 3 | Media | Deploy a producción: usar **digest** (no `:latest`) y añadir **aprobación manual** |
| 4 | Media | Cerrar `AllowedHosts` por env en producción |
| 5 | Baja | Hash de refresh tokens en MongoDB |
| 6 | Baja | Corregir smoke test de staging (variables `STAGING_TEST_*` no definidas) |
| 7 | Baja | Enmascarar email en auditoría de login fallido |
| 8 | Baja | Quitar fallback `dev-seed-secret` y credenciales de seed |
| 9 | Info | Pin imagen Docker por digest + HEALTHCHECK en Dockerfile; reconciliar docs (README/RUNBOOK/dast con rama `main`) |
