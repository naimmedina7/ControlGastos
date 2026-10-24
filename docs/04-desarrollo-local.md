# 04 · Desarrollo local

## Requisitos

- SDK de .NET 10 (`dotnet --list-sdks`).
- Nada más: en desarrollo la base es SQLite, en `src/ControlGastos.Web/App_Data/control-gastos.db`.

## Correr la app

```bash
dotnet run --project src/ControlGastos.Web --launch-profile http
```

Se abre en http://localhost:5238. Al iniciar, la app:
1. Aplica las migraciones pendientes (`Database:MigrarAlIniciar`, por defecto `true`).
2. Solo en `Development`, crea la cuenta de `DatosDeEjemplo` si no existe, con dos tarjetas, las categorías, los gastos del estado de cuenta de octubre 2026 y dos **gastos fijos** desde octubre 2026: préstamo de la moto ($ 5.000) y facultad ($ 9.000).

Para empezar de cero, se detiene la app y se borra `App_Data/control-gastos.db`.

> En esta máquina "Control de aplicaciones" de Windows bloquea los `.exe` generados, por eso los proyectos tienen `UseAppHost=false` y se ejecutan siempre con `dotnet`.

## Configuración

| Clave | Desarrollo | Producción | Qué hace |
|---|---|---|---|
| `Database:Provider` | `Sqlite` | `Postgres` | Motor de base. |
| `ConnectionStrings:Default` | `Data Source=App_Data/control-gastos.db` | variable de entorno | Si está vacía se usa `DATABASE_URL` (formato `postgres://…`). |
| `Database:MigrarAlIniciar` | `true` | `true` | Aplica migraciones al arrancar. |
| `App:ZonaHoraria` | `America/Montevideo` | igual | Define qué es "hoy" y el mes en curso. |
| `App:RegistroHabilitado` | `true` | `true` | `false` cierra el registro del todo. |
| `Seguridad:IntentosPorMinuto` | 10 | 10 | Límite por IP en login y registro. |
| `App:CorreosPermitidos` | vacío (cualquiera) | tu lista | Correos que pueden crear cuenta, separados por coma o punto y coma. |
| `PORT` | — | `8080` en Vercel | Si está definida, la app escucha en ese puerto. |
| `AllowedHosts` | `*` | tu dominio | Hosts aceptados en el encabezado `Host`. |
| `Cotizaciones:Habilitado` | `true` | `true` | Consulta automática del BCU (Frankfurter). Los tests la apagan. |
| `Cotizaciones:Url` | `https://api.frankfurter.dev/` | igual | Base de la API de cotizaciones (por si se usa una instancia propia). |
| `DatosDeEjemplo:Email/Password` | definidos | — | Cuenta de prueba (solo en Development). |

En variables de entorno, `:` se escribe `__` (por ejemplo `ConnectionStrings__Default`).

## Migraciones

Hay **dos juegos de migraciones**, uno por motor, porque los tipos de columna difieren. Cada vez que cambia el modelo hay que generar las dos:

```bash
dotnet tool restore
dotnet ef migrations add NombreDelCambio --project src/ControlGastos.Web --context SqliteAppDbContext --output-dir Data/Migrations/Sqlite
dotnet ef migrations add NombreDelCambio --project src/ControlGastos.Web --context PostgresAppDbContext --output-dir Data/Migrations/Postgres
```

- `dotnet-ef` está fijado en `dotnet-tools.json`, así que no hace falta instalarlo global.
- Las fábricas de [FabricasDeDiseno.cs](../src/ControlGastos.Web/Data/FabricasDeDiseno.cs) permiten generar las migraciones sin conectarse a ninguna base.
- Después de generar, borrar el `design.db` que pueda quedar en `src/ControlGastos.Web`.
- Si Visual Studio tiene la app abierta, bloquea la compilación en Debug: agregar `--configuration Release` al comando.

Migraciones actuales (en los dos motores):

| Migración | Contenido |
|---|---|
| `Inicial` | Identity, tarjetas, categorías, gastos, cuotas, claves de Data Protection |
| `Etapa2GastosFijos` | Tabla `GastosRecurrentes`, `Gastos.GastoRecurrenteId`, `Tarjetas.DiaCierre` |
| `Etapa4TipoCambioPresupuestos` | Tablas `TiposCambio` y `Presupuestos` |
| `Etapa5CotizacionesBcu` | Tabla `CotizacionesBcu` (compartida) |
| `Ingresos` | Tabla `Ingresos` (por usuario, índice por usuario y fecha) |

> En SQLite, `Etapa2GastosFijos` reconstruye la tabla `Gastos` para agregar la FK, y EF avisa que esa parte no corre dentro de una transacción. Es normal. Se probó la actualización de una base de la etapa 1 con datos: se conservaron los gastos y la sesión. `Etapa4TipoCambioPresupuestos` también se aplicó sobre una base existente de la etapa 2.

## Convenciones

- **Idioma**: el código de dominio, la UI y los mensajes están en español; los nombres técnicos del framework, en inglés.
- **Montos**: siempre `decimal`. El formato visible pasa por `Fmt.Money(valor, moneda)` y queda como `$ 1.234,56` o `US$ 12,34`.
- **Fechas con hora**: siempre `DateTime.UtcNow`. Un conversor en `AppDbContext` igual normaliza todo `DateTime` a UTC, porque PostgreSQL rechaza fechas sin zona.
- **Fechas**: `DateOnly`. Los meses se representan con el primer día (`PlanDeCuotas.Periodo`). En la URL van como `?mes=yyyy-MM`.
- **Nuevas entidades del usuario**: implementar `IPerteneceAUsuario` y agregar `HasQueryFilter(x => x.UserId == UsuarioActualId)` en `AppDbContext`. Sumar un test de aislamiento.
- **Pantallas con totales**: agregar `[GenerarRecurrentes]` al controlador, para que los gastos fijos del mes estén cargados.
- **Vistas**: no pegar `@if` a una palabra (`activos@if`), porque Razor lo toma como un email y lo imprime como texto. Separarlo con un espacio o un elemento.
- **Nuevas rutas públicas**: marcar con `[AllowAnonymous]` y agregarlas a la tabla de [03-seguridad.md](03-seguridad.md) y al test de rutas públicas.
- **JS**: nada de scripts inline (lo bloquea la CSP). Agregar el comportamiento en `site.js` (o `reportes.js` para gráficos) y engancharlo con atributos `data-*`.
- **Librerías externas**: siempre con versión fija y `integrity` (SRI). Para calcular el hash: `curl -s URL | openssl dgst -sha384 -binary | openssl base64 -A`.
- **Gráficos**: usar los tokens `--series-1..3` (paleta validada para daltonismo), una sola moneda por gráfico, sin doble eje, y siempre con tabla de datos.
- **Formularios lentos**: no hace falta hacer nada para la ruedita, se aplica a todo `<form>`. Para otro texto, usa `data-texto-carga="Procesando…"` en el botón. Un enlace que descarga un archivo lleva `download data-sin-carga`.
- **Service worker**: al cambiar `site.js` o `site.css`, sube `VERSION` en `wwwroot/sw.js`.
- **Estilos**: las clases base vienen de gastos-equipos (`card-x`, `kpi`, `table-x`, `chip`…); las nuevas están al final de `site.css`.

## Visual Studio

- Abrir `ControlGastos.sln`, poner **ControlGastos.Web** como proyecto de inicio y usar el perfil `http` (puerto 5238).
- Mientras Visual Studio tiene la app abierta, la carpeta `bin/Debug` queda bloqueada. Para correr tests o migraciones desde la terminal sin cerrarla:
  - Tests: `dotnet test --artifacts-path <otra carpeta>`.
  - Migraciones: agregar `--configuration Release` a `dotnet ef migrations add`.

## Vista previa en el editor

`.claude/launch.json` (en la raíz del portfolio) tiene la entrada `control-gastos`, que ejecuta el `dotnet run` de arriba en el puerto 5238.
