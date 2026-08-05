# Guía de Despliegue del Backend BioGuard API en Render.com

Esta guía explica paso a paso cómo desplegar la API REST C# de **BioGuard** en **Render.com** de forma gratuita y automatizada usando Docker.

---

## 🚀 Método 1: Despliegue Automatizado con Blueprint (`render.yaml`) - **Recomendado**

1. **Crear cuenta en Render**:
   Ingresa a [https://dashboard.render.com](https://dashboard.render.com) e inicia sesión con tu cuenta de GitHub (`ap-xlr8`).

2. **Crear nuevo Blueprint**:
   - En el Dashboard de Render, haz clic en **New +** $\rightarrow$ **Blueprint**.
   - Selecciona el repositorio: `ap-xlr8/BIOGUARD_API`.
   - Render detectará automáticamente el archivo [`render.yaml`](file:///c:/Users/alexi/OneDrive/Pictures/POKEMON/EDEL2.0/Backend-BioGuard-master/render.yaml).

3. **Configurar Variables de Entorno Secretas**:
   Render solicitará los valores para las siguientes variables marcadas como secretas:
   * **`ConnectionStrings__MongoDB`**: Cadena de conexión a MongoDB Atlas (ejemplo: `mongodb+srv://usuario:password@cluster.mongodb.net/BioGuardDB?retryWrites=true&w=majority`).
   * **`Jwt__Key`**: Clave secreta para la firma de tokens JWT (cadena aleatoria segura de al menos 32 caracteres).

4. **Desplegar**:
   Haz clic en **Apply**. Render compilará el `Dockerfile` e iniciará el servicio Web automáticamente.

---

## 🛠️ Método 2: Despliegue Manual Web Service en Render

Si prefieres crearlo manualmente sin Blueprint:

1. En el Dashboard de Render, haz clic en **New +** $\rightarrow$ **Web Service**.
2. Conecta el repositorio **`ap-xlr8/BIOGUARD_API`**.
3. Configura los siguientes campos:
   * **Name**: `bioguard-api`
   * **Region**: Oregon (US West) o Frankfurt (EU)
   * **Branch**: `main`
   * **Root Directory**: (dejar en blanco)
   * **Runtime**: **Docker**
   * **Dockerfile Path**: `./Dockerfile`
   * **Instance Type**: **Free**
4. En la sección **Environment Variables**, agrega:
   | Clave | Valor |
   |---|---|
   | `ASPNETCORE_ENVIRONMENT` | `Production` |
   | `PORT` | `8080` |
   | `ASPNETCORE_URLS` | `http://+:8080` |
   | `ConnectionStrings__MongoDB` | *Tu cadena de MongoDB Atlas* |
   | `Jwt__Key` | *Tu clave secreta JWT (mín 32 caracteres)* |
   | `Jwt__Issuer` | `BioGuardApi` |
   | `Jwt__Audience` | `BioGuardApp` |
5. Haz clic en **Create Web Service**.

---

## 🌐 Verificación de Salud
Una vez finalizado el despliegue, Render proporcionará una URL pública (ejemplo: `https://bioguard-api.onrender.com`).

Puedes comprobar que el servicio está activo navegando a:
`https://bioguard-api.onrender.com/health`

Debe responder:
```json
{
  "status": "healthy",
  "database": "connected",
  "timestamp": "2026-08-04T22:00:00Z"
}
```
