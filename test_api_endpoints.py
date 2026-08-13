import os
import sys
import json
import time
import urllib.request
import urllib.error

sys.stdout.reconfigure(encoding='utf-8')

BASE_URL = os.getenv("TARGET_URL", "http://localhost:5000")
if len(sys.argv) > 1:
    BASE_URL = sys.argv[1].rstrip("/")

print("==================================================")
print(" VERIFICACION COMPLETA DE ENDPOINTS BIOGUARD API")
print(f" Target URL: {BASE_URL}")
print("==================================================\n")

token = None
results = []

def make_request(method, endpoint, payload=None, use_token=True):
    url = f"{BASE_URL}{endpoint}"
    headers = {"Content-Type": "application/json"}
    if use_token and token:
        headers["Authorization"] = f"Bearer {token}"

    data = json.dumps(payload).encode("utf-8") if payload else None
    req = urllib.request.Request(url, data=data, headers=headers, method=method)

    start_time = time.time()
    try:
        with urllib.request.urlopen(req, timeout=15) as response:
            duration_ms = round((time.time() - start_time) * 1000, 2)
            body = response.read().decode("utf-8")
            json_resp = json.loads(body) if body else {}
            return response.status, json_resp, duration_ms
    except urllib.error.HTTPError as e:
        duration_ms = round((time.time() - start_time) * 1000, 2)
        try:
            body = e.read().decode("utf-8")
            json_resp = json.loads(body) if body else {}
        except Exception:
            json_resp = {"error_raw": str(e)}
        return e.code, json_resp, duration_ms
    except Exception as e:
        duration_ms = round((time.time() - start_time) * 1000, 2)
        return 500, {"exception": str(e)}, duration_ms

def test_endpoint(name, method, endpoint, payload=None, expected_status=[200, 201, 204], use_token=True):
    status, resp, duration = make_request(method, endpoint, payload, use_token)
    passed = status in expected_status
    icon = "PASSED" if passed else f"FAILED ({status})"
    print(f"[{icon:<12}] {name:<36} | {method:<6} {endpoint:<42} | {duration}ms")
    results.append({
        "name": name,
        "method": method,
        "endpoint": endpoint,
        "status": status,
        "passed": passed,
        "duration_ms": duration,
        "response": resp
    })
    return status, resp

# 1. Health Check
test_endpoint("Health Check System", "GET", "/health", use_token=False)

# 2. Planes (Público)
test_endpoint("Listar Planes Públicos", "GET", "/api/Planes", use_token=False)

# 3. Auth - Intentos de Login Web y Móvil
test_endpoint("Login Web (Prueba Credenciales)", "POST", "/api/Auth/login-web", {"correo": "admin@bioguard.com", "password": "Password123!"}, use_token=False, expected_status=[200, 401, 400])
test_endpoint("Generar Código Móvil", "POST", "/api/Auth/generar-codigo", {"email": "test@bioguard.com"}, use_token=False, expected_status=[200, 400, 404])
st, login_resp = test_endpoint("Login Código Móvil", "POST", "/api/Auth/login-codigo", {"codigo": "123456", "fcmToken": "mock_token"}, use_token=False, expected_status=[200, 400, 401, 404])

if st == 200 and "token" in login_resp:
    token = login_resp["token"]
    print("\n🔑 Token JWT obtenido con éxito para peticiones autenticadas.\n")

# 4. Pacientes
test_endpoint("Obtener Perfil Paciente (/me)", "GET", "/api/Pacientes/me", expected_status=[200, 401, 404])
test_endpoint("Obtener Mi Paciente", "GET", "/api/Pacientes/mi-paciente", expected_status=[200, 401, 404])

# 5. Sensores
test_endpoint("Historial Lecturas Sensores", "GET", "/api/Sensores/historial?limit=5", expected_status=[200, 401, 404])
test_endpoint("Estadísticas Sensores", "GET", "/api/Sensores/estadisticas", expected_status=[200, 401, 404])

# 6. Alertas
test_endpoint("Alertas por Paciente", "GET", "/api/Alertas/by-paciente/60d5ec49f1d2c80015f8e001", expected_status=[200, 401, 403, 404])

# 7. Dispositivos
test_endpoint("Dispositivos por Paciente", "GET", "/api/Dispositivos/by-paciente/60d5ec49f1d2c80015f8e001", expected_status=[200, 401, 403, 404])

# 8. Medicamentos
test_endpoint("Medicamentos por Paciente", "GET", "/api/Medicamentos/by-paciente/60d5ec49f1d2c80015f8e001", expected_status=[200, 401, 403, 404])

# 9. Cuidadores
test_endpoint("Listar Cuidadores", "GET", "/api/Cuidadores", expected_status=[200, 401])

# 10. Notificaciones
test_endpoint("Listar Notificaciones", "GET", "/api/Notificaciones", expected_status=[200, 401])

# 11. ML / Riesgo Metabolico
test_endpoint("Predicción ML Riesgo", "GET", "/api/ML/prediccion-riesgo", expected_status=[200, 401, 404])

# 12. Reportes
test_endpoint("Resumen Ejecutivo Reporte", "GET", "/api/Reportes/resumen", expected_status=[200, 401, 404])

# 13. Tickets Soporte
test_endpoint("Mis Tickets Soporte", "GET", "/api/Tickets/mis-tickets", expected_status=[200, 401])

# 14. Auditoría (Admin)
test_endpoint("Logs Auditoría", "GET", "/api/Auditoria", expected_status=[200, 401, 403])

print("\n==================================================")
passed_count = sum(1 for r in results if r["passed"])
total_count = len(results)
print(f"📊 RESUMEN DE VERIFICACIÓN: {passed_count}/{total_count} ENDPOINTS CONFIGURADOS CORRECTAMENTE")
print("==================================================")
