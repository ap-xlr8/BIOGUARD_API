# BioGuard API - Backend

API RESTful para el ecosistema medico IoT **BioGuard**. Gestiona pacientes con enfermedades metabolicas (diabetes, hipertension), dispositivos WearOS, lecturas de sensores en tiempo real, alertas criticas, predicciones ML y reportes clinicos.

## Arquitectura General

```
                    +-------------------+
                    |   React/Next.js   |  Dashboard Web
                    |   (Web Repo)      |
                    +--------+----------+
                             |
                    +--------v----------+
                    |   .NET 10 API     |  Backend (este repositorio)
                    |   130+ endpoints  |
                    +--------+----------+
                             |
              +--------------+--------------+
              |              |              |
    +---------v---+  +------+--------+  +--+-----------+
    |  Kotlin App  |  |  Wear OS     |  |  Python ML   |
    |  (Movil)     |  |  (WearOS)    |  |  (ML)        |
    |  BLE + SQLite|  |  BLE only    |  |  FastAPI     |
    +--------------+  +--------------+  +--------------+
              |              |              |
              +--------------+--------------+
                             |
                    +--------v----------+
                    |   MongoDB Atlas   |  Base de datos
                    |   18 colecciones  |
                    +-------------------+
```

## Repositorios

| Repositorio | Tecnologia | Descripcion |
|---|---|---|
| **Api-BioGuard** | .NET 10 / C# | Backend API RESTful (este repo) |
| **Movil** | Kotlin / Android | App movil paciente + cuidador |
| **Web** | React / Next.js | Dashboard web para cuidadores |
| **WearOS** | Kotlin / Wear OS | App para reloj WearOS |
| **ML** | Python / FastAPI | Modelo de predicciones ML |

## Stack Tecnologico

| Capa | Tecnologia | Version |
|---|---|---|
| Runtime | .NET | 10.0 |
| Lenguaje | C# | 13 |
| Base de datos | MongoDB Atlas | 7.0+ |
| MongoDB Driver | MongoDB.Driver | 3.10.0 |
| Auth | JWT + PBKDF2 (600K iteraciones) | |
| Tiempo real | SignalR | |
| Email | MailKit | 4.17.0 |
| Rate Limiting | AspNetCoreRateLimit | 5.0.0 |
| Container | Docker | Multi-stage |
| CI/CD | GitHub Actions | |
| Deploy | DigitalOcean App Platform | |
| API Docs | Swagger / OpenAPI | |
| Tests | xUnit + FluentAssertions | 532 tests |

## Funcionalidades

### Modulo 1: Autenticacion y Usuarios
- Registro con verificacion por email (codigo 6-digitos)
- Login web con JWT + Refresh Token rotation
- Login por Google OAuth
- Login por codigo QR (cuidador)
- 2FA por correo electronico
- Recuperacion de password por email
- Bloqueo de cuenta (5 intentos fallidos = 15 min lockout)
- Logout con revocacion de token (blacklist)

### Modulo 2: Pacientes
- CRUD completo de pacientes
- 1 usuario_web = 1 paciente (maximo)
- Edad calculada automaticamente desde fecha de nacimiento
- Foto de perfil

### Modulo 3: Cuidadores y Dispositivos
- CRUD de cuidadores (N por plan)
- Vinculacion de dispositivos WearOS (MAC address)
- Heartbeat / keepalive del reloj
- Conexiones BLE reloj -> telefono -> API

### Modulo 4: Sensores y Lecturas
- Recepcion de lecturas: glucosa, presion, pulso, temperatura, SpO2, peso, GSR
- Batch upload (SQLite offline -> API, max 10MB)
- Estadisticas, tendencias y resumen
- Tracking GPS con historial de ruta
- Eventos metabolicos con atencion medica

### Modulo 5: Alertas
- Alertas criticas (hipoglucemia, hiperglucemia, taquicardia, etc.)
- Creacion automatica por sensores/ML
- Resolucion con accion tomada
- Notificaciones push (FCM)

### Modulo 6: Medicamentos
- CRUD de medicamentos por paciente
- Registro de tomas
- Trigger automatico desde ML cuando detecta pico critico

### Modulo 7: Reportes
- Resumen general del paciente
- Historial de alertas, eventos, medicamentos y lecturas
- Exportar lecturas a CSV

### Modulo 8: Machine Learning
- Predicciones de riesgo metabolico
- Recomendaciones personalizadas
- Entrenamiento y re-entrenamiento de modelos
- Diagnosticos puntuales
- Metricas de modelos

### Modulo 9: Pagos y Planes
- 3 planes: Gratis ($0, 1 cuidador), Familiar ($5 MXN, 3), Pro ($10 MXN, 6)
- Sesiones de pago y historial
- Recibos de pago
- Cancelacion de pagos
- Migracion de precios

### Modulo 10: Web Dashboard
- Perfil de usuario con edicion
- Cambio de plan
- Gestion de correo electronico
- Eliminacion de cuenta

### Modulo 11: Auditoria
- Log de actividades: login, creacion, actualizacion, alertas
- IP y timestamp por accion

## Seguridad

### Autenticacion y Autorizacion
- **JWT** con roles: `dueno`, `paciente`, `cuidador`, `admin`
- **PBKDF2** password hashing (600,000 iteraciones, SHA256, 16-byte salt, 32-byte key)
- **Refresh Token rotation**: cada uso genera un nuevo token y revoca el anterior
- **Token Blacklist**: logout revoca el JTI del JWT
- **Account lockout**: 5 intentos fallidos -> 15 minutos de bloqueo
- **Password complexity**: min 8 chars, mayuscula, minuscula, digito, caracter especial
- **2FA**: codigo de 6 digitos con expiracion temporal
- **Timing-safe comparison**: `CryptographicOperations.FixedTimeEquals` para 2FA y passwords

### Proteccion IDOR
- `OwnershipHelper` verifica que el usuario autenticado sea dueno del paciente en TODOS los endpoints protegidos
- Cuidadores solo acceden a pacientes vinculados (verificacion en DB contra coleccion `cuidadores`)
- Endpoint `Seed` restringido a `admin`

### Headers de Seguridad
- `X-Content-Type-Options: nosniff`
- `X-Frame-Options: DENY`
- `X-XSS-Protection: 1; mode=block`
- `Referrer-Policy: strict-origin-when-cross-origin`
- `Permissions-Policy` (geolocation, camera, microphone deshabilitados)
- `Strict-Transport-Security: max-age=31536000`
- `Content-Security-Policy: default-src 'self'; frame-ancestors 'none'`
- `X-Powered-By` eliminado

### Rate Limiting

| Endpoint | Limite |
|---|---|
| General | 100 req/min |
| Login | 5 req/min |
| Register | 3 req/min |
| 2FA enviar | 3 req/min |
| 2FA verificar | 5 req/min |
| Forgot password | 3 req/min |

### Validacion de Entrada
- DTOs con `[Required]`, `[StringLength]`, `[Range]`, `[EmailAddress]`, `[MinLength]`
- Request size limits: SubirFoto 1MB, Batch 10MB
- Global exception handler (catch-all -> JSON con traceId)

## Estructura del Proyecto

```
BioGuard.Api/
├── Controllers/           # 14 controladores REST
│   ├── AuthController.cs           # Auth + JWT + 2FA + Refresh
│   ├── PacientesController.cs      # CRUD pacientes
│   ├── SensoresController.cs       # Lecturas + GPS + Eventos
│   ├── AlertasController.cs        # Alertas criticas
│   ├── MedicamentosController.cs   # CRUD medicamentos
│   ├── CuidadoresController.cs     # CRUD cuidadores
│   ├── NotificacionesController.cs # Notificaciones push
│   ├── ReportesController.cs       # Reportes + exportar CSV
│   ├── MLController.cs             # Predicciones ML
│   ├── PagosController.cs          # Pagos + recibos
│   ├── PlanesController.cs         # CRUD planes
│   ├── UsuariosWebController.cs    # Perfil de usuario
│   ├── DispositivosController.cs   # WearOS devices
│   └── AuditoriaController.cs      # Logs de auditoria
├── Models/                # 18 modelos MongoDB
│   ├── UsuarioWeb.cs       ├── Paciente.cs
│   ├── Cuidador.cs         ├── Dispositivo.cs
│   ├── LecturaSensor.cs    ├── EventoMetabolico.cs
│   ├── TrackingGps.cs      ├── Alerta.cs
│   ├── Medicamento.cs      ├── Notificacion.cs
│   ├── Pago.cs             ├── Plan.cs
│   ├── PrediccionMl.cs     ├── ModeloMl.cs
│   ├── Auditoria.cs        ├── FcmToken.cs
│   ├── RefreshToken.cs     └── TokenBlacklist.cs
├── Services/              # 15 servicios
│   ├── AuthService.cs             # JWT + PBKDF2 + 2FA + Refresh
│   ├── EmailService.cs            # MailKit SMTP
│   ├── SensorService.cs           # Lecturas + GPS + Eventos
│   ├── AlertaService.cs           # Alertas criticas
│   ├── MedicamentoService.cs      # Medicamentos + tomas
│   ├── PacienteService.cs         # Pacientes
│   ├── CuidadorService.cs         # Cuidadores
│   ├── NotificacionService.cs     # Notificaciones push
│   ├── ReporteService.cs          # Reportes
│   ├── MLService.cs               # ML + predicciones
│   ├── PagosService.cs            # Pagos
│   ├── UsuariosWebService.cs      # Perfil
│   ├── DispositivoService.cs      # WearOS
│   ├── AuditoriaService.cs        # Auditoria
│   └── BioGuardHub.cs             # SignalR hub
├── DTOs/                  # Data Transfer Objects
├── Config/                # MongoDB context + OwnershipHelper
├── Program.cs             # Pipeline: auth, CORS, rate limit, headers
├── appsettings.json       # Configuracion + JWT secrets
├── Dockerfile             # Multi-stage build
└── BioGuard.Api.csproj    # .NET 10 project
```

## Base de Datos (MongoDB Atlas)

### 18 Colecciones

| Coleccion | Descripcion |
|---|---|
| `usuarios_web` | Usuarios duenos y cuidadores |
| `pacientes` | Datos medicos de pacientes |
| `cuidadores` | Relacion cuidador-paciente |
| `dispositivos` | WearOS vinculados |
| `lecturas_sensores` | Glucosa, presion, pulso, etc. (TTL) |
| `eventos_metabolicos` | Hipoglucemia, hiperglucemia, etc. |
| `tracking_gps` | Ubicacion en tiempo real |
| `alertas` | Alertas criticas |
| `medicamentos` | Prescripciones medicas |
| `notificaciones` | Notificaciones push |
| `pagos` | Historial de pagos |
| `planes` | Planes de suscripcion |
| `predicciones_ml` | Predicciones del modelo ML |
| `modelos_ml` | Modelos entrenados |
| `fcm_tokens` | Tokens Firebase Cloud Messaging |
| `refresh_tokens` | Refresh tokens (TTL) |
| `token_blacklist` | JWT revocados (TTL) |
| `auditoria` | Logs de actividad |

### Indices

- **lecturas_sensores**: `{ pacienteId: 1, timestamp: -1 }` + TTL en `expireAt`
- **refresh_tokens**: TTL en `expires_at`
- **token_blacklist**: TTL en `expires_at`
- **Unique indexes**: `correo` en `usuarios_web`, `macAddress` en `dispositivos`

## API Endpoints (85+)

### Auth (10 endpoints)

| Metodo | Ruta | Descripcion | Auth |
|---|---|---|---|
| POST | `/api/Auth/register` | Registro con verificacion email | No |
| POST | `/api/Auth/login-web` | Login web (JWT + RefreshToken) | No |
| POST | `/api/Auth/login-google` | Login Google OAuth | No |
| POST | `/api/Auth/login-codigo` | Login por codigo QR | No |
| POST | `/api/Auth/2FA/enviar` | Enviar codigo 2FA | No |
| POST | `/api/Auth/2FA/verificar` | Verificar 2FA + activar cuenta | No |
| POST | `/api/Auth/forgot-password` | Recuperar password | No |
| POST | `/api/Auth/refresh` | Renovar access token | RefreshToken |
| POST | `/api/Auth/logout` | Revocar token | JWT |
| POST | `/api/Auth/reset-password` | Cambiar password | JWT |

### Pacientes (4 endpoints)

| Metodo | Ruta | Descripcion | Auth |
|---|---|---|---|
| GET | `/api/Pacientes/mi-paciente` | Mi paciente | JWT |
| GET | `/api/Pacientes/{id}` | Paciente por ID | JWT |
| PUT | `/api/Pacientes/{id}` | Actualizar paciente | JWT (dueno) |
| DELETE | `/api/Pacientes/{id}` | Eliminar paciente + cascada | JWT (dueno) |

### Sensores (15 endpoints)

| Metodo | Ruta | Descripcion | Auth |
|---|---|---|---|
| POST | `/api/Sensores/lectura` | Enviar lectura | JWT (paciente_id) |
| POST | `/api/Sensores/lectura-batch` | Lote offline (max 10MB) | JWT (paciente_id) |
| GET | `/api/Sensores/lecturas/{pacienteId}` | Historial lecturas | JWT |
| GET | `/api/Sensores/lecturas/{pacienteId}/rango` | Lecturas por rango fechas | JWT |
| GET | `/api/Sensores/lecturas/{pacienteId}/exportar-pdf` | Exportar CSV | JWT |
| GET | `/api/Sensores/estadisticas/{pacienteId}` | Estadisticas | JWT |
| GET | `/api/Sensores/estadisticas/{pacienteId}/tendencia` | Tendencia | JWT |
| POST | `/api/Sensores/evento` | Crear evento metabolico | JWT (paciente_id) |
| GET | `/api/Sensores/eventos/{pacienteId}` | Historial eventos | JWT |
| GET | `/api/Sensores/eventos/{pacienteId}/resumen` | Resumen eventos | JWT |
| PUT | `/api/Sensores/eventos/{eventoId}/atender` | Atender evento | JWT |
| POST | `/api/Sensores/tracking` | Enviar ubicacion | JWT (paciente_id) |
| POST | `/api/Sensores/tracking-batch` | Lote GPS | JWT (paciente_id) |
| GET | `/api/Sensores/tracking/{pacienteId}/actual` | Ubicacion actual | JWT |
| GET | `/api/Sensores/tracking/{pacienteId}/ruta` | Historial ruta | JWT |

### Alertas (6 endpoints)

| Metodo | Ruta | Descripcion | Auth |
|---|---|---|---|
| POST | `/api/Alertas` | Crear alerta | JWT |
| GET | `/api/Alertas/by-paciente/{pacienteId}` | Alertas del paciente | JWT |
| GET | `/api/Alertas/pendientes/{pacienteId}` | Alertas sin resolver | JWT |
| GET | `/api/Alertas/{id}` | Alerta por ID | JWT |
| PUT | `/api/Alertas/{id}/resolver` | Resolver alerta | JWT |
| DELETE | `/api/Alertas/{id}` | Eliminar alerta | JWT |

### Medicamentos (8 endpoints)

| Metodo | Ruta | Descripcion | Auth |
|---|---|---|---|
| POST | `/api/Medicamentos` | Crear medicamento | JWT (dueno) |
| POST | `/api/Medicamentos/trigger` | Trigger ML | JWT |
| GET | `/api/Medicamentos/by-paciente/{pacienteId}` | Medicamentos del paciente | JWT |
| GET | `/api/Medicamentos/{id}` | Medicamento por ID | JWT |
| PUT | `/api/Medicamentos/{id}` | Actualizar medicamento | JWT |
| PUT | `/api/Medicamentos/{id}/toma` | Registrar toma | JWT |
| PUT | `/api/Medicamentos/{id}/activo` | Activar/desactivar | JWT |
| DELETE | `/api/Medicamentos/{id}` | Eliminar medicamento | JWT |

### Cuidadores (6 endpoints)

| Metodo | Ruta | Descripcion | Auth |
|---|---|---|---|
| GET | `/api/Cuidadores` | Mis cuidadores | JWT |
| GET | `/api/Cuidadores/disponibles` | Cuidadores disponibles | JWT |
| GET | `/api/Cuidadores/by-paciente/{pacienteId}` | Cuidadores del paciente | JWT |
| POST | `/api/Cuidadores` | Agregar cuidador | JWT (dueno) |
| PUT | `/api/Cuidadores/{id}` | Actualizar cuidador | JWT |
| DELETE | `/api/Cuidadores/{id}` | Eliminar cuidador | JWT |

### Reportes (5 endpoints)

| Metodo | Ruta | Descripcion | Auth |
|---|---|---|---|
| GET | `/api/Reportes/resumen/{pacienteId}` | Resumen general | JWT |
| GET | `/api/Reportes/historial-alertas/{pacienteId}` | Historial alertas | JWT |
| GET | `/api/Reportes/historial-eventos/{pacienteId}` | Historial eventos | JWT |
| GET | `/api/Reportes/historial-medicamentos/{pacienteId}` | Historial medicamentos | JWT |
| GET | `/api/Reportes/historial-lecturas/{pacienteId}` | Historial lecturas | JWT |

### ML (8 endpoints)

| Metodo | Ruta | Descripcion | Auth |
|---|---|---|---|
| GET | `/api/ML/predicciones/{pacienteId}` | Predicciones del paciente | JWT |
| GET | `/api/ML/predicciones/{pacienteId}/actual` | Prediccion actual | JWT |
| GET | `/api/ML/recomendaciones/{pacienteId}` | Recomendaciones | JWT |
| GET | `/api/ML/modelos` | Modelos entrenados | JWT |
| GET | `/api/ML/metricas/{modeloId}` | Metricas de modelo | JWT |
| POST | `/api/ML/entrenar` | Entrenar modelo | JWT |
| POST | `/api/ML/reentrenar` | Re-entrenar modelo | JWT |
| POST | `/api/ML/diagnosticar` | Diagnostico puntual | JWT |

### Pagos (4 endpoints)

| Metodo | Ruta | Descripcion | Auth |
|---|---|---|---|
| GET | `/api/Pagos/historial` | Historial de pagos | JWT |
| POST | `/api/Pagos/crear-sesion` | Crear sesion de pago | JWT |
| GET | `/api/Pagos/{id}/recibo` | Recibo de pago | JWT |
| POST | `/api/Pagos/cancelar` | Cancelar pago | JWT |

### Planes (7 endpoints)

| Metodo | Ruta | Descripcion | Auth |
|---|---|---|---|
| GET | `/api/Planes` | Listar planes | No |
| GET | `/api/Planes/{id}` | Plan por ID | No |
| POST | `/api/Planes` | Crear plan | JWT (admin) |
| PUT | `/api/Planes/{id}` | Actualizar plan | JWT (admin) |
| DELETE | `/api/Planes/{id}` | Eliminar plan | JWT (admin) |
| POST | `/api/Planes/seed` | Seed planes | JWT (admin) |
| POST | `/api/Planes/migrate-prices` | Migrar precios | JWT (admin) |

### UsuariosWeb (8 endpoints)

| Metodo | Ruta | Descripcion | Auth |
|---|---|---|---|
| GET | `/api/UsuariosWeb/mi-perfil` | Mi perfil | JWT |
| PUT | `/api/UsuariosWeb/mi-perfil` | Actualizar perfil | JWT |
| PUT | `/api/UsuariosWeb/mi-perfil/correo` | Cambiar correo | JWT |
| PUT | `/api/UsuariosWeb/mi-perfil/foto` | Subir foto (1MB max) | JWT |
| GET | `/api/UsuariosWeb/mi-plan` | Mi plan actual | JWT |
| PUT | `/api/UsuariosWeb/cambiar-plan` | Cambiar plan | JWT |
| GET | `/api/UsuariosWeb/by-email/{correo}` | Buscar por email | JWT |
| DELETE | `/api/UsuariosWeb/mi-cuenta` | Eliminar cuenta | JWT |

### Dispositivos (5 endpoints)

| Metodo | Ruta | Descripcion | Auth |
|---|---|---|---|
| POST | `/api/Dispositivos/vincular` | Vincular WearOS | JWT (paciente_id) |
| POST | `/api/Dispositivos/heartbeat` | Keepalive | JWT (paciente_id) |
| GET | `/api/Dispositivos/{pacienteId}` | Dispositivos del paciente | JWT |
| PUT | `/api/Dispositivos/{id}` | Actualizar dispositivo | JWT |
| DELETE | `/api/Dispositivos/{id}` | Desvincular dispositivo | JWT |

### Auditoria (1 endpoint)

| Metodo | Ruta | Descripcion | Auth |
|---|---|---|---|
| GET | `/api/Auditoria` | Logs de actividad | JWT |

### Notificaciones (2 endpoints)

| Metodo | Ruta | Descripcion | Auth |
|---|---|---|---|
| GET | `/api/Notificaciones` | Mis notificaciones | JWT |
| POST | `/api/Notificaciones` | Crear notificacion | JWT |

### Seed (1 endpoint)

| Metodo | Ruta | Descripcion | Auth |
|---|---|---|---|
| POST | `/api/Seed/seed-all` | Insertar datos de prueba | JWT (admin) |

### Health (1 endpoint)

| Metodo | Ruta | Descripcion | Auth |
|---|---|---|---|
| GET | `/health` | Health check | No |

## Despliegue

### Documentacion de Operaciones

| Documento | Contenido |
|---|---|
| `docs/RUNBOOK_PRODUCCION.md` | Despliegue, monitoreo, troubleshooting y rollback |
| `docs/ROTACION_SECRETOS.md` | Rotacion de claves y secretos por entorno |

### URL de Produccion

```
https://bioguard-api-lkvnq.ondigitalocean.app/
```

### Docker

```bash
# Build
docker build -t bioguard-api .

# Run
docker run -p 5000:8080 \
  -e MONGODB_URI="mongodb+srv://..." \
  -e MONGODB_DATABASE="bioguard" \
  -e JWT_SECRET_KEY="tu-clave-secreta" \
  -e SMTP_HOST="smtp.gmail.com" \
  -e SMTP_PORT="587" \
  -e SMTP_USER="tu@email.com" \
  -e SMTP_PASSWORD="tu-password" \
  -e SMTP_FROM="tu@email.com" \
  bioguard-api
```

### Variables de Entorno (Requeridas)

| Variable | Descripcion |
|---|---|
| `MONGODB_URI` | Connection string MongoDB Atlas |
| `MONGODB_DATABASE` | Nombre de la base de datos |
| `JWT_SECRET_KEY` | Clave secreta para JWT (min 32 chars) |
| `SMTP_HOST` | Servidor SMTP |
| `SMTP_PORT` | Puerto SMTP |
| `SMTP_USER` | Usuario SMTP |
| `SMTP_PASSWORD` | Password SMTP |
| `SMTP_FROM` | Email remitente |

### CI/CD Pipeline (GitHub Actions)

1. **Build & Test**: Compila, corre 532 tests, genera coverage
2. **CodeQL Analysis**: Analisis estatico de seguridad
3. **Docker Build**: Build multi-stage, push a GitHub Container Registry
4. **Deploy**: DigitalOcean App Platform (auto-deploy desde master)

### Branching Strategy

- `master`: rama principal (protegida, requiere PR + 2 status checks + 1 approval)
- `rama-Liz`: rama de desarrollo activa
- PR merge a master -> deploy automatico a produccion

## Testing

### Ejecutar Tests

```bash
cd Test1BioGuard
dotnet test --verbosity minimal
```

### Tipos de Tests

| Tipo | Cantidad | Descripcion |
|---|---|---|
| Unit Tests | ~200 | Servicios aislados con mocks |
| Integration Tests | ~100 | Endpoints HTTP completos |
| Security Tests | ~80 | IDOR, auth, input validation, timing |
| Load Tests | ~40 | Rate limiting, batch processing |

### Credenciales de Prueba (Seed)

```
Email:    seed_639204600292413571@bioguard.test
Password: SeedTest@123!
Paciente: 6a62d9fd3e0a61f86c97f916
Rol:      dueno
```

### Seed Endpoint

```bash
POST /api/Seed/seed-all
Authorization: Bearer <admin_token>

# Inserta todos los datos de prueba si las colecciones estan vacias
# Retorna { "inserted": {...}, "skipped": [...] }
```

## Changelog Reciente (PR #56)

### Fixes en PR #56 (rama-Liz)

| Fix | Descripcion |
|---|---|
| **Refresh Token 500** | `CryptographicOperations.FixedTimeEquals` no puede ser traducido por MongoDB LINQ provider. Reemplazado por comparacion de strings en queries de BD |
| **Auditoria 500** | Documentos legacy con `entidad_id` como ObjectId fallan al deserializar. Wrapper try-catch que retorna lista vacia |

### Fixes en PR #55

| Fix | Descripcion |
|---|---|
| **Refresh Token en login** | Los 4 metodos de login ahora crean RefreshToken en DB |
| **Alerta accionTomada** | Campo `accion_tomada` guardado correctamente en MongoDB |
| **ExportarPDF real** | Retorna CSV descargable con datos reales |
| **Pagos Recibo real** | Retorna datos reales del pago |
| **Cascade delete** | Eliminar paciente tambien elimina cuidadores asociados |
| **OwnershipHelper** | Logica de verificacion extraida a clase compartida (era copy-paste en 7 controllers) |

### Fixes en PR #54

| Fix | Descripcion |
|---|---|
| **Email verification** | Registro crea usuario inactivo, codigo 6-digitos, verificacion activa cuenta |
| **Forgot password** | Ahora envia email real via MailKit |
| **MailKit upgrade** | 4.11.0 -> 4.17.0 (vulnerabilidad GHSA-9j88-vvj5-vhgr) |

### Fixes en PR #52

| Fix | Descripcion |
|---|---|
| **Auditoria EntidadId** | Removido `[BsonRepresentation(BsonType.ObjectId)]` que causaba 500 en inserts |

### Seguridad Agregada

- IDOR protection via `OwnershipHelper` en 7 controllers
- Cuidador ownership verification en todos los endpoints
- Role-based auth `[Authorize(Roles = "dueno")]` en endpoints de escritura
- Timing-safe 2FA comparison
- Account lockout (5 intentos / 15 min)
- Password complexity validation (8+ chars, mayuscula, minuscula, digito, especial)
- CSP + security headers
- Rate limiting per-endpoint
- Request size limits (1MB foto, 10MB batch)
- Token blacklist + refresh token rotation
- 2FA enforcement en `UsuarioWeb`
