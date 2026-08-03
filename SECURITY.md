# BioGuard Security Policy

## Supported Versions

| Version | Supported |
|---------|-----------|
| Latest (master) | Yes |
| < Latest | No |

## Reporting a Vulnerability

If you discover a security vulnerability in BioGuard, please report it responsibly.

**DO NOT** open a public GitHub issue for security vulnerabilities.

### How to Report

1. Email: **security@bioguard.app** (or contact repository maintainer directly)
2. Include:
   - Description of the vulnerability
   - Steps to reproduce
   - Potential impact
   - Suggested fix (if any)

### Response Timeline

| Phase | SLA |
|-------|-----|
| Acknowledgment | 48 hours |
| Initial assessment | 5 business days |
| Fix or mitigation | 30 business days |
| Public disclosure | After fix is deployed |

## Security Measures

### Authentication & Authorization
- JWT tokens with HMAC-SHA256 signing
- Refresh token rotation with single-use enforcement
- Token revocation via blacklist (logout, password change)
- Account lockout after 5 failed login attempts (15-minute cooldown)
- Two-factor authentication (2FA) via email codes
- Password complexity: min 8 chars, uppercase, lowercase, digit, special character
- PBKDF2-SHA256 with 600,000 iterations for password hashing
- Role-based access control (dueno, paciente, cuidador, admin)
- IDOR protection with ownership verification on all endpoints

### API Security
- CORS restricted to specific origins, methods, and headers
- Rate limiting per endpoint (login: 5/min, register: 3/min, 2FA: 3/min)
- Request size limits (1MB for photos, 10MB for batch endpoints)
- Security headers: CSP, HSTS, X-Frame-Options, X-XSS-Protection, X-Content-Type-Options
- Content-Security-Policy with `default-src 'self'; frame-ancestors 'none'`
- X-Powered-By header removed
- AllowedHosts configured for production

### Data Protection
- MongoDB Atlas with TLS encryption in transit
- Sensitive data encrypted at rest (Atlas default)
- Access codes (QR) hidden from API responses via `[JsonIgnore]`
- No secrets in source code or configuration files
- `env.example` contains only placeholder values

### Infrastructure Security
- Docker: non-root user, production environment, minimal base image
- GitHub Actions: SHA-pinned actions, least-privilege permissions
- Container scanning with Trivy (CRITICAL/HIGH severity)
- SBOM generation for supply chain transparency
- Container image signing with cosign (Sigstore)

### DevSecOps Pipeline
| Stage | Tool | Purpose |
|-------|------|---------|
| SAST | CodeQL | Static code analysis |
| SCA | Dependabot + NuGet Audit | Dependency vulnerability scanning |
| DAST | OWASP ZAP | Dynamic API testing |
| Secret Scanning | Gitleaks | Prevent secret leaks |
| Container Scan | Trivy | OS/library vulnerabilities |
| License Compliance | dotnet-list-package | Block copyleft licenses |
| SBOM | Anchore | Supply chain documentation |
| Image Signing | Cosign | Container integrity verification |

## Scope

This policy applies to:
- **API Backend** (Api-BioGuard) - .NET 10 Web API
- **Docker Container** - Production deployment image
- **CI/CD Pipeline** - GitHub Actions workflows

Out of scope:
- Mobile apps (Kotlin/WearOS) - separate security policies
- Web dashboard (React/Next.js) - separate security policies
- ML model repository

## Acknowledgments

We appreciate the security research community and will acknowledge reporters who follow responsible disclosure.
