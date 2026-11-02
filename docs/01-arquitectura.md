# 01 · Arquitectura

## Stack

| Capa | Tecnología | Motivo |
|---|---|---|
| Frontend | Vistas Razor (MVC) + Bootstrap 5 + CSS propio + JS sin framework; Chart.js 4.4.1 solo en Reportes | Se reutilizó el diseño de gastos-equipos. No hay build de frontend ni una SPA para mantener. |
| Backend | ASP.NET Core MVC, .NET 10 | Una sola app sirve el HTML y la lógica, así que hay un solo deploy. |
| Datos | EF Core 10: **SQLite** en desarrollo, **PostgreSQL** en producción | SQLite no requiere instalar nada; PostgreSQL es gratis en Neon y robusto. |
| Autenticación | ASP.NET Core Identity + cookie de autenticación | Hash de contraseñas, bloqueo por intentos y security stamp, ya resueltos y probados. |
| Hosting | Contenedor Docker (Render) + Postgres administrado (Neon) | Cuesta US$0–7 por mes y se despliega desde git. |

## Capas y carpetas

```
src/ControlGastos.Web/
├── Domain/            Entidades y reglas puras (sin EF ni HTTP)
│   ├── Entidades.cs           AppUser, Tarjeta, Categoria, Gasto, Cuota, GastoRecurrente, Ingreso, enums
│   ├── PlanDeCuotas.cs        Períodos, mes de facturación (día de cierre) y generación de cuotas
│   ├── EstadoDeCuenta.cs      Parser del texto del estado de cuenta, normalización de comercios, reglas de categoría
│   └── CategoriasPorDefecto.cs
├── Data/              Persistencia
│   ├── AppDbContext.cs        Mapeo, filtros por usuario, control de dueño al guardar
│   ├── UsuarioActual.cs       De dónde sale el UserId (token o fijo)
│   ├── FabricasDeDiseno.cs    Solo para `dotnet ef`
│   ├── DatosDeEjemplo.cs      Cuenta de prueba (solo en Development)
│   └── Migrations/{Sqlite,Postgres}/
├── Services/          Casos de uso
│   ├── GastoService.cs        Crear, editar, borrar, resumen del mes, proyección, planes activos
│   ├── RecurrenteService.cs   Gastos fijos: alta, pausa, borrado, generación mensual, previstos
│   ├── ReporteService.cs      Serie mensual por tipo, por categoría y medio de pago, tipo de cambio, movimientos para CSV
│   ├── ImportacionService.cs  Análisis (sugerencias, duplicados) e importación en una transacción
│   ├── PresupuestoService.cs  Presupuestos por categoría y su avance en el mes
│   ├── CotizacionService.cs   Cotización mensual del BCU (Frankfurter), guardada en la base
│   ├── IngresoService.cs      Ingresos del mes y balance contra los gastos ("cuánto te queda")
│   └── RespaldoService.cs     Exportar y restaurar los datos de un usuario (JSON)
│   └── Reloj.cs               "Hoy" en la zona horaria de Uruguay
├── Infrastructure/    Configuración transversal
│   ├── ConfiguracionBase.cs   Elección de motor y cadena de conexión
│   ├── EncabezadosSeguridad.cs
│   ├── ErroresIdentityEnEspanol.cs
│   ├── GenerarRecurrentesAttribute.cs  Genera los fijos del mes antes de mostrar totales
│   ├── ExportadorCsv.cs       CSV para Excel (separador ;, coma decimal, BOM, sin inyección de fórmulas)
│   └── ComandosAdmin.cs       `admin usuarios | reset-password | desbloquear` por consola
│   └── Fmt.cs                 Formato es-UY (montos, meses) y piezas de UI
├── Controllers/       Inicio (público: muestra el login), Cuenta, Panel, Gastos, Ingresos, Recurrentes (/fijos),
│                      Reportes, Importar, TipoCambio, Presupuestos, Tarjetas, Categorias
├── ViewModels/        Formularios con validación (DataAnnotations)
├── Views/             Razor; Shared/ tiene el layout, los parciales (_Balance, _Dona, _VistaVacia…) y el logo
└── wwwroot/           site.css (diseño, ver 11-diseno.md), site.js (interacciones), reportes.js (gráficos),
                       fonts/ (Bricolage Grotesque), Bootstrap,
                       manifest.webmanifest + sw.js + offline.html + icons/ (app instalable)
```

Reglas de dependencia:
- `Domain` no depende de nada.
- `Data` depende de `Domain`.
- `Services` depende de `Data` y `Domain`.
- Los controladores usan servicios para la lógica de gastos y el `DbContext` directamente para ABM simples (tarjetas, categorías, ingresos).

## Flujo de una request privada

```
Navegador ──cookie .ControlGastos.Auth──▶ Kestrel
  → ForwardedHeaders (HTTPS detrás del proxy)
  → Encabezados de seguridad (CSP, X-Frame-Options…)
  → Routing → RateLimiter → Authentication (descifra la cookie → ClaimsPrincipal)
  → Authorization: AuthorizeFilter global (sin sesión → 302 a /cuenta/login)
  → Antiforgery (solo POST)
  → [GenerarRecurrentes] (GET en panel, gastos, fijos, tarjetas): crea los gastos fijos que falten hasta el mes en curso
  → Controller → GastoService / RecurrenteService → AppDbContext
       └─ cada consulta lleva WHERE UserId = @usuarioActual (filtro global)
  → Vista Razor (_Layout) → HTML
```

## Cálculo de montos

- Toda la plata es `decimal` con 2 decimales (`HasPrecision(14, 2)`), nunca `double`.
- Las sumas se hacen **en memoria** después de traer las cuotas del período, porque SQLite no suma `decimal` en SQL. El volumen de un usuario (decenas de cuotas por mes) lo hace irrelevante. Si se pasara a Postgres solamente, se podría mover a SQL.
- Pesos y dólares **nunca se suman entre sí**: todos los totales se agrupan por moneda.

## Gastos fijos sin procesos en segundo plano

Los gastos fijos no necesitan un job programado. Antes de mostrar una pantalla con totales, el filtro `[GenerarRecurrentes]` llama a `RecurrenteService.GenerarPendientesAsync(hasta)`, que crea los gastos de los meses que faltan:
- `hasta` es el mes en curso o, si la pantalla muestra un mes futuro (`?mes=2027-02`), ese mes, con un tope de 24 meses (`RecurrenteService.HastaQueMes`). Así, al avanzar a febrero, los fijos de febrero ya están cargados y suman.
- Es idempotente: cada recurrente guarda `GeneradoHasta`, y hay un índice único (recurrente, estado de cuenta).
- Con tarjeta de crédito, dos meses pueden caer en el mismo estado de cuenta (si cambió el día de cierre, o un día 31 en un mes de 30). Ese gasto se **salta** en lugar de chocar con el índice.
- **Cada fijo se guarda por separado**: si uno falla, los demás se cargan igual, y el error queda como advertencia en el log. (Antes se guardaban todos juntos y un choque de un fijo de crédito frenaba la carga de todos los demás, sin avisar.)
- Cuesta una sola consulta cuando no hay nada pendiente.

Más allá de lo generado, los meses futuros se muestran como **previstos** en la vista del mes y se suman a la proyección del panel.

## Front-end

- No hay scripts inline: la CSP solo permite `site.js` y jsDelivr. Las interacciones se enganchan por atributos `data-*`:
  - `data-confirm`: pide confirmación antes de enviar.
  - `data-autosubmit`: envía el formulario al cambiar el valor (selector de mes).
  - `data-toggle-pass`: muestra u oculta la contraseña.
  - `data-table-tools`: filtro y búsqueda en tablas.
  - `#form-gasto`: resumen en vivo del plan de cuotas.
  - `[data-filter]` en la tabla del mes: filtros por tarjeta y categoría, combinados con tipo y búsqueda, con total recalculado de lo visible.
  - `#form-gasto[data-sugerencias]`: autocompletado de descripciones usadas, en JSON dentro de un atributo (la CSP no permite JSON inline en `<script>`).
  - `#form-tarjeta`: muestra el día de cierre solo para crédito.
  - `details` plegados: si la validación encuentra un error adentro (por ejemplo, en "Más detalles" del formulario de gasto), lo abre antes de enfocar el campo.
  - `[data-requisitos]`: en el registro, marca los requisitos de la contraseña mientras se escribe (mismas reglas que Identity).
  - `[data-dona]`: tooltip y resaltado de la gráfica circular por categoría. El texto se arma con `textContent`.
- El aviso del estado de cuenta en el formulario repite en JS la regla de `PlanDeCuotas.PeriodoDeFacturacion`. Si cambia una, hay que cambiar la otra.
- **Indicador de carga** (`site.js`, para la latencia de Vercel):
  - Al enviar un formulario, el botón presionado muestra una ruedita y un segundo envío se ignora, lo que evita gastos duplicados por doble clic. El botón no se deshabilita (`disabled`) porque el navegador dejaría de enviar su valor: "Guardar y cargar otro" depende de eso.
  - Al navegar por un enlace, aparece una barra fina arriba de la pantalla si la página tarda más de 250 ms. Un aviso `role="status"` informa "Cargando…" a los lectores de pantalla.
  - Los enlaces de descarga (`download data-sin-carga`) no activan la barra. Al volver con "atrás", todo se limpia (`pageshow`).
  - El texto del botón se puede cambiar con `data-texto-carga` (por ejemplo "Importando…").
- El botón flotante `.fab` (por debajo de 992 px) lleva a "Nuevo gasto" desde cualquier pantalla, salvo en los formularios y en Ingresos.
- El gráfico del panel sigue hecho con CSS (sin JS, liviano). La **gráfica circular** por categoría (`_Dona`) es SVG generado en el servidor, sin Chart.js, y se usa en Gastos del mes y en Reportes. Los demás gráficos interactivos están en **Reportes**, con Chart.js.
- El sistema visual (tokens, tipografía, componentes, accesibilidad) está en [11-diseno.md](11-diseno.md).

## Servicios externos

La única llamada a un servicio externo desde el servidor es la cotización del dólar: `CotizacionService` usa un `HttpClient` tipado (`AddHttpClient`) contra `https://api.frankfurter.dev/`, con timeout de 6 s. El resultado se guarda en la tabla `CotizacionesBcu`, así la API se consulta pocas veces y la app funciona aunque no responda. Ver [10-administracion.md](10-administracion.md#cotizaciones-del-dólar).

## Modo consola

Si la app arranca con `admin …` como argumento, `Program.cs` ejecuta el comando ([ComandosAdmin.cs](../src/ControlGastos.Web/Infrastructure/ComandosAdmin.cs)) y termina, sin levantar el servidor web ni aplicar migraciones.

## App instalable (PWA)

- `manifest.webmanifest`: nombre, íconos (normal y "maskable"), color, inicio en `/panel` y accesos directos a *Nuevo gasto* y *Gastos del mes*.
- `sw.js` (service worker): las páginas van **siempre a la red**, nunca se guardan; solo se guardan los estáticos (`/css`, `/js`, `/lib`, `/icons`, `/fonts`) y `offline.html`. Así no queda ningún dato financiero en el dispositivo.
- Para los estáticos usa primero la **versión exacta** pedida (`site.css?v=…`); la que ignora la versión solo se usa sin conexión. Así un CSS nuevo nunca queda tapado por uno viejo. Igual hay que subir `VERSION` al cambiar `site.css` o `site.js`.
- La CSP no cambió: el manifest y el service worker son del mismo origen (`'self'`).

## Reportes (Chart.js)

- Chart.js 4.4.1 se carga **solo en `/reportes`**, desde jsDelivr y con **SRI** (`integrity="sha384-…"`): si el archivo del CDN cambiara, el navegador no lo ejecuta.
- Los datos viajan como JSON en `data-datos` de cada `<canvas>`, y `reportes.js` los dibuja. No hay scripts inline.
- Las decisiones de diseño siguen la guía de visualización:
  - **Gasto por mes**: columnas apiladas por tipo (contado, cuotas, fijos) con la paleta validada para daltonismo (`--series-1..3`) y 2 px de separación entre segmentos. Solo el segmento superior tiene la punta redondeada. Los meses futuros se ven en tono más claro y el tooltip dice "proyección".
  - **Por categoría**: gráfica circular con el color de cada categoría, como mucho 6 porciones (las 5 más grandes y "Otras"), leyenda con % y monto, y tooltip con el nombre al pasar el mouse (ver [11-diseno.md](11-diseno.md#gráfica-circular-por-categoría)).
  - **Por medio de pago**: barras horizontales de **un solo color**, con el valor en la punta.
  - **Una sola moneda por vista**: pesos, dólares o todo en pesos. Nunca dos ejes.
  - Cada gráfico tiene su **tabla de datos** (`<details>`) y una leyenda en HTML.
- Es responsive: la barra lateral se pliega por debajo de 992 px y a 375 px no hay scroll horizontal.
