# Mis·Gastos

Aplicación web de control de gastos personales: tarjetas, compras en cuotas, pesos y dólares. Cada usuario ve **solo sus datos**: las cuentas están aisladas entre sí, las contraseñas se guardan con hash y todas las rutas son privadas salvo la portada, el login y el registro.

Hecha con ASP.NET Core MVC (.NET 10), EF Core e Identity. El diseño se reutilizó de [gastos-equipos](../gastos-equipos/README.md).

## Estado

| Etapa | Contenido | Estado |
|---|---|---|
| 0 · Base | Solución, EF Core (SQLite / PostgreSQL), Identity, diseño | ✅ Hecha |
| 1 · MVP | Login, registro, tarjetas, categorías, gastos con cuotas, vista mensual, panel | ✅ Hecha |
| 2 · Uso rápido | Gastos fijos, día de cierre, carga rápida, filtros, cuotas pendientes | ✅ Hecha |
| 3 · Dashboards | Reportes con gráficos por mes, tipo, categoría y medio de pago; exportar CSV | ✅ Hecha |
| 4 · Comodidad | Importar estado de cuenta, tipo de cambio, presupuestos | ✅ Hecha |
| 5 · Publicación y respaldos | Revisión de seguridad, cotización automática del BCU, respaldo y restauración, comandos de administración, app instalable, `Dockerfile.vercel` | ✅ Hecha (falta el primer deploy) |

El detalle está en [docs/07-hoja-de-ruta.md](docs/07-hoja-de-ruta.md).

## Cómo correrlo

Requisitos: SDK de .NET 10. No hace falta instalar ninguna base de datos, porque en desarrollo se usa SQLite.

```bash
dotnet run --project src/ControlGastos.Web --launch-profile http
```

Después, abrir http://localhost:5238. La primera vez se crea la base en `src/ControlGastos.Web/App_Data/` y una **cuenta de prueba** con los gastos de octubre 2026 (y el préstamo de la moto y la facultad como gastos fijos). Su correo y su contraseña están en `src/ControlGastos.Web/appsettings.Development.json`, en la sección `DatosDeEjemplo`.

Tests:

```bash
dotnet test
```

## Documentación

| Documento | Qué cuenta |
|---|---|
| [01 · Arquitectura](docs/01-arquitectura.md) | Stack, capas, carpetas y flujo de una request |
| [02 · Modelo de datos](docs/02-modelo-de-datos.md) | Entidades, relaciones, cómo se calculan las cuotas, monedas |
| [03 · Seguridad](docs/03-seguridad.md) | Autenticación, hash de contraseñas, rutas privadas, aislamiento, CSRF, encabezados |
| [04 · Desarrollo local](docs/04-desarrollo-local.md) | Configuración, migraciones, datos de prueba, convenciones |
| [05 · Despliegue](docs/05-despliegue.md) | Vercel (contenedor) + Neon, alternativa Render, variables de entorno, checklist |
| [06 · Pruebas](docs/06-pruebas.md) | Qué cubren los tests y cómo agregar nuevos |
| [07 · Hoja de ruta](docs/07-hoja-de-ruta.md) | Etapas, pendientes y deuda técnica |
| [08 · Decisiones](docs/08-decisiones.md) | Por qué se eligió cada cosa |
| [09 · Guía de uso](docs/09-guia-de-uso.md) | Cómo importar el estado de cuenta, cargar cuotas y gastos fijos, y usar reportes y presupuestos |
| [10 · Administración](docs/10-administracion.md) | Contraseñas olvidadas, respaldos, cotizaciones y app instalable |

## Estructura

```
control-gastos/
├── src/ControlGastos.Web/       App MVC (dominio, datos, servicios, controladores, vistas)
├── tests/ControlGastos.Tests/   xUnit: cuotas, gastos fijos, aislamiento y seguridad HTTP (154 tests)
├── docs/                        Documentación
├── Dockerfile                   Imagen de producción (Render, Docker local)
├── Dockerfile.vercel            Imagen para Vercel (Container Runtime)
├── tools/generar_iconos.py      Íconos de la app instalable
├── render.yaml                  Blueprint para Render
└── dotnet-tools.json            dotnet-ef como herramienta local
```
