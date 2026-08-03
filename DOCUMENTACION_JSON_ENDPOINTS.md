# BioGuard API — Ejemplos de Consumo JSON de Endpoints

Esta guía contiene ejemplos estructurados de entrada (Request Body) y salida (Response Payload) para consumir cada uno de los endpoints de la API de **BioGuard**.

---

## 1. AuthController (`/api/Auth`)

### POST `/api/Auth/register`
* **Entrada (JSON):**
  ```json
  {
    "nombre": "Carlos",
    "apellidoPaterno": "Perez",
    "apellidoMaterno": "Gomez",
    "correo": "carlos@example.com",
    "password": "Password123!",
    "planNombre": "Gratis"
  }
  ```
* **Salida (JSON):**
  ```json
  {
    "message": "Usuario registrado exitosamente"
  }
  ```

### POST `/api/Auth/login-web`
* **Entrada (JSON):**
  ```json
  {
    "correo": "carlos@example.com",
    "password": "Password123!"
  }
  ```
* **Salida (JSON):**
  ```json
  {
    "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJ1c2VySWQiOiI2NmYxYTJiM2M0ZDVlNmY3YThiOWMwZDEiLCJyb2wiOiJkdWVubyJ9...",
    "userId": "66f1a2b3c4d5e6f7a8b9c0d1",
    "nombre": "Carlos",
    "rol": "dueno",
    "plan": "Gratis",
    "requires2FA": false
  }
  ```

### POST `/api/Auth/login-codigo`
* **Entrada (JSON):**
  ```json
  {
    "codigoAcceso": "ABC12345"
  }
  ```
* **Salida (JSON):**
  ```json
  {
    "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
    "refreshToken": "d7a8b9c0d1e2...",
    "userId": "66f1a2b3c4d5e6f7a8b9c0e2",
    "nombre": "Paciente Juan",
    "rol": "paciente"
  }
  ```

### POST `/api/Auth/login-google`
* **Entrada (JSON):**
  ```json
  {
    "idToken": "eyJhbGciOiJSUzI1NiIsImtpZCI6..."
  }
  ```
* **Salida (JSON):**
  ```json
  {
    "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
    "userId": "66f1a2b3c4d5e6f7a8b9c0d1",
    "nombre": "Carlos Google",
    "rol": "dueno",
    "plan": "Gratis"
  }
  ```

### POST `/api/Auth/enviar-2fa`
* **Entrada (JSON):**
  ```json
  {
    "correo": "carlos@example.com"
  }
  ```
* **Salida (JSON):**
  ```json
  {
    "message": "Código de verificación 2FA enviado al correo"
  }
  ```

### POST `/api/Auth/verificar-2fa`
* **Entrada (JSON):**
  ```json
  {
    "correo": "carlos@example.com",
    "codigoOtp": "123456"
  }
  ```
* **Salida (JSON):**
  ```json
  {
    "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
    "userId": "66f1a2b3c4d5e6f7a8b9c0d1",
    "nombre": "Carlos",
    "rol": "dueno"
  }
  ```

### POST `/api/Auth/refresh`
* **Entrada (JSON):**
  ```json
  {
    "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
    "refreshToken": "d7a8b9c0d1e2..."
  }
  ```
* **Salida (JSON):**
  ```json
  {
    "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.new...",
    "refreshToken": "e8b9c0d1e2f3..."
  }
  ```

### POST `/api/Auth/forgot-password`
* **Entrada (JSON):**
  ```json
  {
    "correo": "carlos@example.com"
  }
  ```
* **Salida (JSON):**
  ```json
  {
    "message": "Si el correo está registrado, recibirás un link de recuperación"
  }
  ```

### POST `/api/Auth/reset-password`
* **Entrada (JSON):**
  ```json
  {
    "token": "token_recuperacion_123",
    "correo": "carlos@example.com",
    "nuevaPassword": "NewPassword123!"
  }
  ```
* **Salida (JSON):**
  ```json
  {
    "message": "Contraseña restablecida correctamente"
  }
  ```

### PUT `/api/Auth/cambiar-password`
* **Entrada (JSON):**
  ```json
  {
    "passwordActual": "Password123!",
    "nuevaPassword": "NewPassword123!"
  }
  ```
* **Salida (JSON):**
  ```json
  {
    "message": "Contraseña cambiada exitosamente"
  }
  ```

### POST `/api/Auth/logout`
* **Entrada:** N/A (Headers con Bearer Token).
* **Salida (JSON):**
  ```json
  {
    "message": "Sesión cerrada correctamente"
  }
  ```

---

## 2. UsuariosWebController (`/api/UsuariosWeb`)

### GET `/api/UsuariosWeb/mi-perfil`
* **Salida (JSON):**
  ```json
  {
    "id": "66f1a2b3c4d5e6f7a8b9c0d1",
    "nombre": "Carlos Perez",
    "correo": "carlos@example.com",
    "fechaRegistro": "2026-07-30T10:00:00Z",
    "planId": "plan_gratis",
    "planNombre": "Gratis"
  }
  ```

### PUT `/api/UsuariosWeb/mi-perfil`
* **Entrada (JSON):**
  ```json
  {
    "nombre": "Carlos Mario",
    "apellidoPaterno": "Perez",
    "apellidoMaterno": "Gomez"
  }
  ```
* **Salida (JSON):**
  ```json
  {
    "message": "Perfil actualizado con éxito"
  }
  ```

### PUT `/api/UsuariosWeb/mi-perfil/correo`
* **Entrada (JSON):**
  ```json
  {
    "nuevoCorreo": "carlos.nuevo@example.com",
    "password": "Password123!"
  }
  ```
* **Salida (JSON):**
  ```json
  {
    "message": "Correo actualizado. Inicie sesión de nuevo"
  }
  ```

### GET `/api/UsuariosWeb/mi-plan`
* **Salida (JSON):**
  ```json
  {
    "planId": "plan_familiar",
    "nombre": "Familiar",
    "limitePacientes": 3,
    "limiteCuidadores": 5,
    "retencionHistorialDias": 90,
    "gpsActivo": true,
    "consolaIaActiva": true
  }
  ```

### GET `/api/UsuariosWeb/mis-sesiones`
* **Salida (JSON):**
  ```json
  [
    {
      "id": "sesion_id_123",
      "dispositivo": "Chrome / Windows 11",
      "ip": "192.168.1.10",
      "ultimaActividad": "2026-07-30T23:00:00Z",
      "esActual": true
    }
  ]
  ```

---

## 3. PacientesController (`/api/Pacientes`)

### POST `/api/Pacientes`
* **Entrada (JSON):**
  ```json
  {
    "nombre": "Juan Perez",
    "esDiabetico": true
  }
  ```
* **Salida (JSON):**
  ```json
  {
    "pacienteId": "66f1a2b3c4d5e6f7a8b9c0d2",
    "codigoAccesoQr": "PAC12345",
    "message": "Paciente registrado"
  }
  ```

### PUT `/api/Pacientes/{id}/biometria`
* **Entrada (JSON):**
  ```json
  {
    "fechaNacimiento": "1996-07-30T00:00:00Z",
    "sexo": "M",
    "pesoKg": 75.5,
    "estaturaCm": 175.0,
    "esDiabetico": true,
    "familiaresDiabetes": true,
    "actividadFisica": "Moderada"
  }
  ```
* **Salida (JSON):**
  ```json
  {
    "message": "Biometría actualizada"
  }
  ```

### GET `/api/Pacientes/{id}/dashboard-summary`
* **Salida (JSON):**
  ```json
  {
    "paciente": {
      "id": "66f1a2b3c4d5e6f7a8b9c0d2",
      "nombre": "Juan Perez",
      "esDiabetico": true,
      "perfilCompletado": true
    },
    "ultimaLectura": {
      "timestamp": "2026-07-30T22:00:00Z",
      "pulsoBpm": 80,
      "temperaturaC": 36.5,
      "sudoracionGsr": 1.8,
      "hrv": 55,
      "spo2": 98,
      "probabilidadPico": 0.05,
      "nivelRiesgo": "Bajo"
    },
    "ultimaUbicacion": {
      "longitud": -99.1332,
      "latitud": 19.4326,
      "timestamp": "2026-07-30T22:01:00Z",
      "esEmergencia": false
    },
    "dispositivo": {
      "vinculado": true,
      "nombreDispositivo": "Galaxy Watch 6",
      "macAddress": "AA:BB:CC:DD:EE:FF",
      "conectado": true
    },
    "alertasPendientesCount": 0,
    "alertasRecientes": [],
    "eventosRecientes": []
  }
  ```

---

## 4. DispositivosController (`/api/Dispositivos`)

### POST `/api/Dispositivos/vincular`
* **Entrada (JSON):**
  ```json
  {
    "nombre": "Galaxy Watch Active 2",
    "macAddress": "00:1A:2B:3C:4D:5E"
  }
  ```
* **Salida (JSON):**
  ```json
  {
    "dispositivoId": "66f1a2b3c4d5e6f7a8b9c0d3",
    "message": "Dispositivo vinculado"
  }
  ```

### GET `/api/Dispositivos/{pacienteId}/info-completa`
* **Salida (JSON):**
  ```json
  {
    "reloj": {
      "modelo": "Galaxy Watch Active 2",
      "conectado": true,
      "bateria": 85,
      "ultimaSincronizacion": "2026-07-30T23:30:00Z",
      "sensoresDisponibles": ["pulso", "gsr", "spo2"]
    },
    "telefono": {
      "modelo": "Google Pixel 7a",
      "sistemaOperativo": "Android 13",
      "bateria": 92,
      "ahorroEnergia": false,
      "conectividad": "wifi"
    }
  }
  ```

---

## 5. CuidadoresController (`/api/Cuidadores`)

### POST `/api/Cuidadores`
* **Entrada (JSON):**
  ```json
  {
    "pacienteId": "66f1a2b3c4d5e6f7a8b9c0d2",
    "nombre": "Sofia Gomez",
    "parentesco": "Hija",
    "telefono": "+525512345678",
    "correo": "sofia@example.com",
    "nivelAcceso": "historial_completo"
  }
  ```
* **Salida (JSON):**
  ```json
  {
    "cuidadorId": "66f1a2b3c4d5e6f7a8b9c0d4",
    "codigoAccesoQr": "CUI98765",
    "message": "Cuidador creado"
  }
  ```

---

## 6. MedicamentosController (`/api/Medicamentos`)

### POST `/api/Medicamentos`
* **Entrada (JSON):**
  ```json
  {
    "pacienteId": "66f1a2b3c4d5e6f7a8b9c0d2",
    "nombre": "Metformina",
    "dosis": "850 mg",
    "frecuencia": "Cada 12 horas",
    "notas": "Tomar junto con la comida"
  }
  ```
* **Salida (JSON):**
  ```json
  {
    "medicamentoId": "66f1a2b3c4d5e6f7a8b9c0d5",
    "message": "Medicamento prescrito correctamente"
  }
  ```

---

## 7. SensoresController (`/api/Sensores`)

### POST `/api/Sensores/lecturas`
* **Entrada (JSON):**
  ```json
  [
    {
      "pulsoBpm": 82,
      "temperaturaC": 36.4,
      "sudoracionGsr": 1.9,
      "hrv": 58,
      "spo2": 98,
      "timestamp": "2026-07-30T23:40:00Z"
    }
  ]
  ```
* **Salida (JSON):**
  ```json
  {
    "message": "Lecturas procesadas"
  }
  ```

### POST `/api/Sensores/tracking`
* **Entrada (JSON):**
  ```json
  {
    "latitud": 19.4326,
    "longitud": -99.1332,
    "esEmergencia": false
  }
  ```
* **Salida (JSON):**
  ```json
  {
    "message": "Ubicación registrada"
  }
  ```

---

## 8. AlertasController (`/api/Alertas`)

### POST `/api/Alertas`
* **Entrada (JSON):**
  ```json
  {
    "pacienteId": "66f1a2b3c4d5e6f7a8b9c0d2",
    "tipoAlerta": "pulso_alto",
    "descripcion": "Pulso cardíaco detectado en 145 lpm en reposo",
    "latitud": 19.4326,
    "longitud": -99.1332
  }
  ```
* **Salida (JSON):**
  ```json
  {
    "alertaId": "66f1a2b3c4d5e6f7a8b9c0d6",
    "message": "Alerta de emergencia activada y notificaciones enviadas"
  }
  ```

### POST `/api/Alertas/{id}/atender`
* **Entrada (JSON):**
  ```json
  {
    "notasAtencion": "Se contactó al paciente y se encuentra estable."
  }
  ```
* **Salida (JSON):**
  ```json
  {
    "message": "Alerta marcada como atendida"
  }
  ```

---

## 9. PagosController (`/api/Pagos`)

### POST `/api/Pagos/crear-sesion`
* **Entrada (JSON):**
  ```json
  {
    "planNombre": "Familiar",
    "procesador": "stripe"
  }
  ```
* **Salida (JSON):**
  ```json
  {
    "pagoId": "66f1a2b3c4d5e6f7a8b9c0d7",
    "monto": 129.00,
    "moneda": "MXN",
    "sesionUrl": "https://checkout.stripe.com/pay/cs_test_..."
  }
  ```

### GET `/api/Pagos/historial`
* **Salida (JSON):**
  ```json
  [
    {
      "id": "pago_id_123",
      "monto": 129.00,
      "moneda": "MXN",
      "estado": "completado",
      "fechaPago": "2026-07-30T22:15:00Z",
      "metodoPago": "stripe"
    }
  ]
  ```
