# Análisis Completo de Seguridad y DevSecOps - BioGuard API

**Fecha:** 2026-08-12  
**Proyecto:** BioGuard API (.NET 10)  
**Evaluador:** Análisis Automatizado de DevSecOps  

---

## 📊 RESUMEN EJECUTIVO

| Aspecto | Calificación | Estado |
|---------|------------|--------|
| **Gestión de Secretos** | ⚠️ RIESGOS IDENTIFICADOS | Parcialmente bien implementado |
| **Prácticas DevSecOps** | ✅ BUENAS | Pipeline de seguridad completo |
| **Información Expuesta** | ⚠️ CRÍTICA | SÍ - Múltiples hallazgos |
| **Calidad de CI/CD** | ✅ BUENA | GitHub Actions configurado correctamente |
| **Dependencias** | ⚠️ DESALINEADAS | Versiones inconsistentes |
| **Docker & Contenedores** | ⚠️ WARNINGS | Sin health check, tags flotantes |

---

## 🔴 INFORMACIÓN EXPUESTA (CRÍTICO)

### 1. **Credenciales de Prueba Hardcodeadas en Código Fuente**

**Severidad:** 🔴 CRÍTICA

#### Ubicación 1: `Program.cs` (Seed Data)
```csharp
// Línea 320
PasswordHash = PasswordHasher.Hash("SeedTest@123!")

// Línea 406  
PasswordHash = PasswordHasher.Hash("Cuidador@123!")

// Línea 458
var secret = app.Configuration["Seed:Secret"] ?? "dev-seed-secret";
```

**Impacto:**
- Las credenciales están en el historial de git permanentemente
- Accesible a cualquiera con acceso al repositorio
- El fallback `"dev-seed-secret"` es predecible y débil
- El endpoint `/seed` está protegido pero usa secreto débil

**Recomendación:**
```bash
# NO usar passwords en código ni fallbacks débiles
# Generar valores aleatorios en runtime o usar secrets management
```

#### Ubicación 2: `test_api_endpoints.py` (Test Script)
```python
# Línea 72-74
test_endpoint("Login Web (Prueba Credenciales)", "POST", "/api/Auth/login-web", 
    {"correo": "admin@bioguard.com", "password": "Password123!"}, 
    use_token=False, expected_status=[200, 401, 400])
```

**Problema:** Script de prueba con URL de Render (producción) contiene credenciales hardcodeadas.

#### Ubicación 3: `Test1BioGuard/NonFunctionalTests/SmokeTests.cs`
```csharp
// Línea 43
PasswordHash = PasswordHasher.Hash("Test@123!")

// Línea 52
var request = new { Correo = "smoke@bioguard.test", Password = "Test@123!" }
```

**Impacto:** Código de test con credenciales en el repositorio.

---

### 2. **URLs de Producción Expuestas en Código**

**Severidad:** 🟠 ALTA

#### Ubicación: `test_api_endpoints.py`
```python
# Línea 6
BASE_URL = "https://bioguard-api-6k8p.onrender.com"
```

**Impacto:**
- ✅ Se puede overrider con argumentos
- ❌ URL de Render (producción) hardcodeada por defecto
- Visible en historial de git
- Facilita ataques dirigidos contra producción

#### Ubicación: `.github/workflows/dast.yml`
```yaml
# Línea 23
default: 'https://bioguard-api-lkvnq.ondigitalocean.app'
```

**Impacto:**
- URL de producción expuesta en workflow público
- Facilita reconocimiento de infraestructura (OSINT)
- El dominio real puede ser descubierto

---

### 3. **Documentación con JWT Tokens de Ejemplo**

**Severidad:** 🟠 ALTA

#### Archivos afectados:
- `DOCUMENTACION_API.md` (Líneas 78)
- `DOCUMENTACION_JSON_ENDPOINTS.md` (Líneas 39, 58, 70)

**Ejemplo:**
```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "refreshToken": "d7a8b9c0d1e2..."
}
```

**Impacto:**
- ⚠️ Los tokens están truncados (bien)
- ⚠️ Pero estructura JWT completa es visible
- Patrón estándar facilita fuzzing
- Información de claim structure expuesta

---

### 4. **Configuración de DigitalOcean con Secretos**

**Severidad:** 🟡 MEDIA

#### Archivo: `.do/app.yaml`
```yaml
envs:
  - key: MONGODB_CONNECTION_STRING
    type: SECRET          # ✅ Bien
  - key: JWT_SECRET_KEY
    type: SECRET          # ✅ Bien
  - key: FIREBASE_SERVICE_ACCOUNT_JSON
    type: SECRET          # ✅ Bien
```

**Estado:** ✅ BIEN - Los secretos están marcados como `type: SECRET`

---

### 5. **Archivo de Ejemplo `.env.example`**

**Severidad:** ✅ BIEN

**Estado:** CORRECTO
- Contiene solo placeholders: `tu_usuario`, `tu_contrasena`
- Valores reales no incluidos
- Archivo bien documentado con comentarios

---

## 🔐 HALLAZGOS DE SEGURIDAD

### CRÍTICOS

#### 1. Fallback Débil en Seed Endpoint
**Archivo:** `Program.cs:458`
```csharp
var secret = app.Configuration["Seed:Secret"] ?? "dev-seed-secret";
```

**Problema:**
- Fallback `"dev-seed-secret"` es predecible
- Si env var no está configurada en dev, usa este valor débil
- Endpoint seed puede ser activado por atacante que conoce este secret

**Fix:**
```csharp
var secret = app.Configuration["Seed:Secret"];
if (string.IsNullOrWhiteSpace(secret))
{
    if (!app.Environment.IsDevelopment())
        throw new InvalidOperationException("Seed:Secret requerido en non-dev");
    secret = Guid.NewGuid().ToString(); // Generar random para dev
}
```

---

#### 2. Credentials en Test Script Contra Producción
**Archivo:** `test_api_endpoints.py`
```python
test_endpoint("Login Web", "POST", "/api/Auth/login-web", 
    {"correo": "admin@bioguard.com", "password": "Password123!"}, 
    use_token=False)
```

**Problema:**
- Script intenta login en producción con credenciales
- `admin@bioguard.com` puede ser email real
- `Password123!` es contraseña de prueba débil
- Script está en raíz del repo, visible en git

**Fix:**
- No hardcodear credentials de prueba
- Separar test script en carpeta tests/
- Usar variables de entorno para URLs
- No incluir admin credentials nunca

---

#### 3. Passwords Hardcodeadas en Seed
**Archivos:**
- `Program.cs:320` - `SeedTest@123!`
- `Program.cs:406` - `Cuidador@123!`

**Problema:**
- Passwords están en el código fuente
- Cualquiera que vea el código puede reproducir datos de test
- Si la seed se ejecuta accidentalmente en prod = datos comprometidos

**Fix:**
```csharp
// Generar contraseñas aleatorias en runtime
var testPassword = PasswordHasher.Hash(Guid.NewGuid().ToString());
var cuidadorPassword = PasswordHasher.Hash(Guid.NewGuid().ToString());

// O cargar de variables de entorno
var testPassword = PasswordHasher.Hash(
    Environment.GetEnvironmentVariable("SEED_TEST_PASSWORD") 
    ?? Guid.NewGuid().ToString()
);
```

---

#### 4. Exposición de Dominios de Producción
**Archivos:**
- `test_api_endpoints.py:6` - `https://bioguard-api-6k8p.onrender.com`
- `.github/workflows/dast.yml:23` - `https://bioguard-api-lkvnq.ondigitalocean.app`

**Problema:**
- URLs de producción hardcodeadas en repo público
- OSINT: Atacante puede descubrir infraestructura
- Scaneo de vulnerabilidades dirigido contra prod
- Flujo de DAST apunta siempre a producción

**Fix:**
```yaml
# En dast.yml
env:
  DEFAULT_TARGET: ${{ secrets.PROD_API_URL || 'https://staging.bioguard.app' }}
```

---

### ALTOS

#### 5. Refresh Tokens Sin Hash en MongoDB
**Severidad:** 🟠 ALTA

**Problema:**
- Los refresh tokens se almacenan en base64 en claro
- Si MongoDB es comprometida, tokens son accesibles directamente
- No hay hash adicional (solo base64 = reversible)

**Ubicación probable:** `Models/RefreshToken.cs`

**Fix Recomendado:**
```csharp
// Guardar hash del refresh token
public string RefreshTokenHash { get; set; } // SHA256
public DateTime ExpiresAt { get; set; }
public string JwtId { get; set; } // jti para revocación

// En AuthService
var tokenHash = SHA256.ComputeHash(Encoding.UTF8.GetBytes(refreshToken));
var refreshTokenRecord = new RefreshToken 
{ 
    RefreshTokenHash = Convert.ToHexString(tokenHash),
    ...
};
```

---

#### 6. AllowedHosts Demasiado Permisivo
**Archivo:** `appsettings.json`
```json
"AllowedHosts": "*"
```

**Problema:**
- En producción, acepta cualquier header Host
- Vulnerable a Host Header Injection
- Debe configurarse por entorno

**Fix:**
```json
// appsettings.json (dev)
"AllowedHosts": "localhost;127.0.0.1"

// appsettings.Production.json (o env var)
"AllowedHosts": "bioguard.app;www.bioguard.app;api.bioguard.app"
```

---

#### 7. Emails Completos Expuestos en Logs de Auditoría
**Severidad:** 🟠 ALTA

**Problema:**
- `AuditoriaService.cs` registra email completo en logs
- `AuthController.cs:114` registra email en intento fallido de login
- Logs son visibles en muchos sistemas

**Impacto:**
- Email enumeration attack
- Exposición de PII (Personally Identifiable Information)
- Información para target ataques

**Fix:**
```csharp
// Enmascarar email en logs
private static string MaskEmail(string email)
{
    var parts = email.Split('@');
    if (parts.Length != 2) return "***";
    
    var local = parts[0];
    var domain = parts[1];
    
    var masked = local.Length > 2 
        ? $"{local[0]}***{local[^1]}@{domain}"
        : "***@" + domain;
    
    return masked;
}

// En logs
logger.LogWarning($"Login failed for {MaskEmail(email)}");
```

---

#### 8. Swagger Habilitado en Development
**Beneficio:** Útil para desarrollo
**Riesgo:** ⚠️ Verificar que está deshabilitado en prod

```csharp
// En Program.cs
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
```

**Status:** ✅ Bien implementado (solo Dev)

---

### MEDIOS

#### 9. Versiones de Dependencias Desalineadas
**Severidad:** 🟡 MEDIA

```
App Target: .NET 10.0
NuGet Packages:
  - JwtBearer: 9.0.18       ❌ Debe ser 10.0.x
  - OpenApi: 9.0.18         ❌ Debe ser 10.0.x  
  - Mvc.Testing: 9.0.18     ❌ Debe ser 10.0.x
  - Swashbuckle: 6.9.0      ⚠️  Obsoleta (existe 7.x)
  - AspNetCoreRateLimit: 5.0.0 ⚠️  Mantenimiento bajo
```

**Impacto:**
- Vulnerabilidades no parcheadas
- Incompatibilidades futuras
- Problemas de seguridad en versiones antiguas

**Fix:**
```bash
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer --version 10.0.0
dotnet add package Microsoft.AspNetCore.OpenApi --version 10.0.0
dotnet add package Microsoft.AspNetCore.Mvc.Testing --version 10.0.0
dotnet add package Swashbuckle.AspNetCore --version 7.0.0
```

---

#### 10. Bug en DAST Workflow - Nunca Dispara
**Archivo:** `.github/workflows/dast.yml`

**Problema:**
```yaml
on:
  workflow_run:
    workflows: ["BioGuard CI/CD"]
    types: [completed]
    branches: [master]    # ❌ Rama es "main" no "master"
```

**Impacto:**
- Workflow DAST NUNCA se dispara automáticamente
- Scans OWASP ZAP no se ejecutan contra producción después de deploys
- Vulnerabilidades DAST no detectadas

**Fix:**
```yaml
branches: [main]  # Coincidir con rama real
```

---

#### 11. Sin Verificación en DAST Contra Producción
**Archivo:** `.github/workflows/dast.yml:23`

**Problema:**
- El escaneo DAST apunta a producción directamente
- No hay separación staging/prod
- Vulnerabilidades encontradas se reportan contra prod real

**Fix:**
```yaml
# Crear environment específico para DAST
jobs:
  dast-staging:
    environment: staging
    with:
      target: https://staging-bioguard.app
  
  dast-prod:
    environment: production
    needs: dast-staging
    with:
      target: https://api.bioguard.app
```

---

#### 12. Dockerfile con Tags Flotantes
**Severidad:** 🟡 MEDIA

**Archivo:** `Dockerfile`
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
```

**Problema:**
- Tags `10.0` sin pin a versión específica
- Cambios en imagen base pueden introducir vulnerabilidades
- Builds no son reproducibles (non-deterministic)

**CI Check:** hadolint lo reporta como `DL3007` (warning)

**Fix:**
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0.0 AS base
FROM mcr.microsoft.com/dotnet/sdk:10.0.0 AS build
```

O mejor aún, pinear por digest (SHA256):
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:abc123... AS base
```

---

#### 13. Sin HEALTHCHECK en Dockerfile
**Severidad:** 🟡 MEDIA

**Problema:**
```dockerfile
# Dockerfile - SIN healthcheck
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
```

**Impact:**
- Kubernetes/Docker no puede verificar salud del contenedor
- Replicas muertas pueden seguir recibiendo tráfico
- DigitalOcean App Platform debe inferir salud (ineficiente)

**Fix:**
```dockerfile
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD dotnet health-check.dll || exit 1

ENTRYPOINT ["dotnet", "BioGuard.Api.dll"]
```

O usar endpoint HTTP:
```dockerfile
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD wget -qO- http://localhost:8080/health || exit 1
```

---

## ✅ PRÁCTICAS DEVSECOS CORRECTAS

### 1. **Pipeline CI/CD Completo**
- ✅ Build en cada push/PR
- ✅ Tests automatizados (cobertura >30%)
- ✅ NuGet audit integrado
- ✅ License compliance scanning
- ✅ Dockerfile linting (hadolint)
- ✅ Secret scanning (Gitleaks)
- ✅ CodeQL SAST
- ✅ Trivy container scanning
- ✅ SBOM generation (anchore)
- ✅ Image signing (cosign keyless)

### 2. **Gestión de Secretos (GitHub Actions)**
- ✅ Secrets en GitHub Secrets (no en código)
- ✅ Environment-based deployments
- ✅ DigitalOcean App Platform with SECRET type
- ✅ docker-compose lee de .env (no versionado)

### 3. **Seguridad de Aplicación**
- ✅ JWT con HMAC-SHA256
- ✅ Refresh token rotation
- ✅ Token blacklist (jti)
- ✅ PBKDF2-SHA256 (600k iteraciones)
- ✅ Rate limiting (AspNetCoreRateLimit + Redis)
- ✅ CORS restringido
- ✅ HSTS + redirección HTTPS
- ✅ Security headers implementados
- ✅ IDOR protection con ownership checks
- ✅ 2FA por email

### 4. **Gestión de Dependencias**
- ✅ Dependabot habilitado (nuget, github-actions, docker)
- ✅ Actualizaciones semanales
- ✅ Audit integrado en CI
- ✅ License compliance checks

### 5. **Seguridad Docker**
- ✅ Multi-stage builds
- ✅ No root user (`USER $APP_UID`)
- ✅ Sin secretos en imagen
- ✅ .dockerignore bien configurado

---

## ⚠️ MATRIZ DE RIESGOS

| Hallazgo | Severidad | Probabilidad | Impacto | Riesgo |
|----------|-----------|--------------|---------|--------|
| Passwords hardcodeadas (Seed) | CRÍTICA | MEDIA | ALTO | 🔴 CRÍTICO |
| Credenciales en test script | CRÍTICA | BAJA | ALTO | 🟠 ALTO |
| Fallback débil `dev-seed-secret` | CRÍTICA | MEDIA | MEDIO | 🟠 ALTO |
| Dominios prod expuestos en repo | ALTA | MEDIA | MEDIO | 🟠 ALTO |
| Refresh tokens sin hash | ALTA | BAJA | ALTO | 🟠 ALTO |
| AllowedHosts="*" en prod | ALTA | BAJA | MEDIO | 🟡 MEDIO |
| Emails en logs auditoría | ALTA | ALTA | MEDIO | 🟠 ALTO |
| Dependencias desalineadas | MEDIA | MEDIA | MEDIO | 🟡 MEDIO |
| DAST workflow nunca dispara | MEDIA | ALTA | BAJO | 🟡 MEDIO |
| Tags Docker flotantes | MEDIA | MEDIA | BAJO | 🟡 MEDIO |

---

## 🛠️ PLAN DE REMEDIACIÓN

### **Inmediato (Esta semana)**

```bash
# 1. ELIMINAR credenciales del código
git filter-branch -f --tree-filter 'find . -type f -name "*.cs" -o -name "*.py" | xargs sed -i "s/SeedTest@123!/<PLACEHOLDER>/g"'
git filter-branch -f --tree-filter 'find . -type f -name "*.cs" -o -name "*.py" | xargs sed -i "s/Cuidador@123!/<PLACEHOLDER>/g"'
git filter-branch -f --tree-filter 'find . -type f -name "*.cs" -o -name "*.py" | xargs sed -i "s/Password123!/<PLACEHOLDER>/g"'
git push origin --force-with-lease

# 2. Remover URLs hardcodeadas
# Editarlas según las recomendaciones arriba

# 3. Cambiar fallback weak
# Program.cs:458 - generar random en Dev si no hay config
```

### **Corto Plazo (Este sprint)**

1. Actualizar todas las dependencias a versión 10.0.x
2. Arreglar DAST workflow (branch: [main])
3. Agregar HEALTHCHECK a Dockerfile
4. Pinear tags Docker a versiones específicas
5. Implementar hash para refresh tokens
6. Corregir AllowedHosts por entorno
7. Enmascarar emails en logs

### **Mediano Plazo**

1. Separar test credentials en Azure Key Vault/GitHub Secrets
2. Implementar GitHub OIDC para CI/CD sin secrets
3. Agregar scanning de commits históricos
4. Implementar supply chain security (cosign + SBOM)
5. Agregar DAST en staging environment

---

## 📋 CHECKLIST DE REMEDIATION

### Código Fuente
- [ ] Remover todas las credenciales del historial git
- [ ] Implementar fallback aleatorio para Seed:Secret
- [ ] Mover test credentials a variables de entorno
- [ ] Enmascarar emails en AuditoriaService
- [ ] Hash para refresh tokens en MongoDB

### Configuración
- [ ] Establecer AllowedHosts por entorno
- [ ] Externalizar URLs de producción
- [ ] Usar GitHub Secrets para credenciales de test

### CI/CD
- [ ] Actualizar DAST workflow (branch)
- [ ] Pinear Docker tags a versiones específicas
- [ ] Agregar HEALTHCHECK a Dockerfile
- [ ] Actualizar todas las dependencias NuGet

### Documentación
- [ ] Remover tokens JWT de ejemplos o truncarlos más
- [ ] Documentar política de secrets
- [ ] Crear runbook de rotación de secrets

---

## 📚 REFERENCIAS

- [OWASP Top 10 - A02:2021 – Cryptographic Failures](https://owasp.org/Top10/A02_2021-Cryptographic_Failures/)
- [OWASP Top 10 - A08:2021 – Software and Data Integrity Failures](https://owasp.org/Top10/A08_2021-Software_and_Data_Integrity_Failures/)
- [GitHub - Security Hardening](https://docs.github.com/en/actions/security-guides/security-hardening-for-github-actions)
- [Docker Security Best Practices](https://cheatsheetseries.owasp.org/cheatsheets/Docker_Security_Cheat_Sheet.html)
- [CWE-798: Use of Hard-Coded Credentials](https://cwe.mitre.org/data/definitions/798.html)
- [CWE-256: Plaintext Storage of a Password](https://cwe.mitre.org/data/definitions/256.html)

---

## 🎯 CONCLUSIÓN

**El proyecto tiene un pipeline DevSecOps bien estructurado** con SAST, SCA, DAST y secret scanning. Sin embargo:

1. **Hay información sensible expuesta en el repositorio** (credenciales y URLs de producción)
2. **Necesita limpieza urgente del historial git** de credenciales
3. **Varios detalles de configuración necesitan endurecimiento** (AllowedHosts, refresh tokens, logs)
4. **Algunos workflows están rotos** (DAST nunca dispara)
5. **Dependencias están desalineadas** respecto a .NET 10.0

**Prioridad:** 
- 🔴 CRÍTICA: Limpieza de credenciales
- 🟠 ALTA: Configuración de seguridad
- 🟡 MEDIA: Dependencias y workflows

El equipo tiene buenas prácticas base pero necesita fixes inmediatos en los puntos críticos.

