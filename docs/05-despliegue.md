# 05 · Despliegue

Opción elegida: **Vercel** (la app, como contenedor Docker) + **Neon** (PostgreSQL, desde el Marketplace de Vercel). Alternativa equivalente: **Render** + Neon (ver más abajo). Los precios y límites de los planes gratuitos cambian: conviene revisarlos al momento de publicar.

> Ni el `Dockerfile` ni las migraciones de PostgreSQL se probaron todavía en esta máquina, porque no tiene Docker ni Postgres. Lo que sí se verificó es que `dotnet publish -c Release` compila. El primer deploy es la prueba real de ambos.

## Vercel: qué hay que saber

Vercel no tiene un runtime propio de .NET. Lo que sí tiene es el **Container Runtime**, que corre un `Dockerfile.vercel` como Vercel Function. Según su documentación (julio 2026), está **en Beta en todos los planes**, incluido Hobby (gratuito, uso personal y no comercial).

Cómo encaja esta app:

| Característica de Vercel | Cómo lo resuelve la app |
|---|---|
| El contenedor **escala a cero** a los 5 minutos sin tráfico y no tiene disco persistente | La base es Postgres (Neon), nunca SQLite. Las claves que cifran la cookie se guardan en la base (Data Protection), así que la sesión sobrevive a cada arranque. |
| No hay procesos en segundo plano | No hacen falta: los gastos fijos se generan al abrir la app (decisión D11). |
| El puerto llega en la variable `PORT` (por defecto 80) | La app escucha en `$PORT` si existe (`Program.cs`). Configurar `PORT=8080`, porque la imagen corre sin root y no puede abrir el 80. |
| TLS y proxy en el borde | `UseForwardedHeaders` toma el esquema HTTPS y la IP del cliente. Vercel reemplaza `X-Forwarded-For` con la IP real, así que no se puede falsificar. |
| Cuerpo de request de hasta ~4,5 MB | La importación más grande (30.000 caracteres) ocupa unos 60 KB. |
| Arranque en frío | La primera visita después de 5 minutos de inactividad tarda unos segundos más (arranque de .NET y de Neon, que también se duerme). |

### Pasos

1. **Archivos de despliegue**:
   - [Dockerfile.vercel](../Dockerfile.vercel), en la raíz de `control-gastos/`: igual al `Dockerfile`, con `PORT=8080`.
   - `vercel.json` no hace falta: Vercel detecta el `Dockerfile.vercel` y enruta todo el tráfico al contenedor.
2. Subir el repositorio a GitHub e importarlo en Vercel. Si el repo es el portfolio entero, poner *Root Directory* = `proyectos/control-gastos`.
3. En **Storage → Marketplace**, agregar **Neon** (plan gratuito). Vercel inyecta `DATABASE_URL` y la app la convierte sola ([ConfiguracionBase.cs](../src/ControlGastos.Web/Infrastructure/ConfiguracionBase.cs)).
4. Variables de entorno del proyecto:

| Variable | Valor |
|---|---|
| `PORT` | `8080` |
| `DATABASE_URL` | La pone Neon (o `ConnectionStrings__Default` con formato Npgsql) |
| `App__CorreosPermitidos` | Tu correo y los de las personas que van a usarla, separados por coma |
| `App__RegistroHabilitado` | `true` (con la lista de correos alcanza; ponerlo en `false` cierra el registro del todo) |
| `AllowedHosts` | Tu dominio, por ejemplo `mis-gastos.vercel.app` |

5. Deploy. Las tablas se crean solas: la app aplica las migraciones al arrancar.
6. Entrar a `/cuenta/registro` con un correo de la lista y crear la cuenta.

## Render (alternativa)

1. **New → Blueprint** con [render.yaml](../render.yaml), o **New → Web Service → Docker**.
2. Mismas variables que en Vercel, salvo `PORT`: Render usa el 8080 de `ASPNETCORE_HTTP_PORTS`.
3. El plan gratuito también se duerme por inactividad.

## Neon a mano (si no se usa el Marketplace)

1. Crear un proyecto en Neon, en una región cercana a la de la app.
2. Copiar la *connection string*: `postgresql://usuario:clave@host/base?sslmode=require`.
3. Pegarla tal cual en `DATABASE_URL`, o convertirla a `Host=HOST;Database=BASE;Username=USUARIO;Password=CLAVE;SSL Mode=Require` para `ConnectionStrings__Default`.

## Docker local (opcional)

```bash
docker build -t mis-gastos .
docker run -p 8080:8080 -e ConnectionStrings__Default="Host=...;Database=...;Username=...;Password=...;SSL Mode=Require" mis-gastos
```

## Checklist antes de usarla en serio

- [ ] HTTPS activo y HSTS respondiendo.
- [ ] `App__CorreosPermitidos` con la lista de correos, o el registro cerrado.
- [ ] `AllowedHosts` con el dominio real.
- [ ] La cadena de conexión solo en variables de entorno, nunca en el repo.
- [ ] Descargar un primer respaldo desde *Mi perfil* y probar que el comando `admin usuarios` llega a la base ([10-administracion.md](10-administracion.md)).
- [ ] Instalar la app en el celular y verificar que abre en `/panel`.
- [ ] Probar el login desde el celular y que la sesión siga abierta después de un redeploy (claves en la base).
- [ ] Revisar `dotnet list package --vulnerable` antes de cada deploy.
