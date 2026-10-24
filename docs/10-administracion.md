# 10 · Administración

Tareas de mantenimiento para cuando la app está publicada y la usan tú y tu entorno: contraseñas olvidadas, respaldos y cotizaciones.

## Comandos de administración (consola)

La app no envía emails, así que una contraseña olvidada se resuelve con un comando. Vercel no permite entrar a la consola del contenedor, así que **se ejecuta desde tu PC**, apuntando a la base de producción. El comando no levanta el servidor web: hace la tarea y termina.

```powershell
# En la carpeta proyectos/control-gastos, con la cadena de conexión de Neon (no la guardes en ningún archivo):
$env:ASPNETCORE_ENVIRONMENT = "Production"
$env:Database__Provider = "Postgres"
$env:ConnectionStrings__Default = "Host=...;Database=...;Username=...;Password=...;SSL Mode=Require"
dotnet run --project src/ControlGastos.Web -- admin usuarios
```

| Comando | Qué hace |
|---|---|
| `admin usuarios` | Lista las cuentas: correo, fecha de alta y si está bloqueada. |
| `admin reset-password <correo>` | Genera una **contraseña temporal** aleatoria (por ejemplo `k7m2-p9qx-4tn8-w3hz`), quita el bloqueo y cierra las sesiones abiertas de esa cuenta en los próximos minutos. |
| `admin desbloquear <correo>` | Quita el bloqueo por intentos fallidos, sin cambiar la contraseña. |

Después de un `reset-password`:
1. Pásale la temporal a la persona por un canal privado (no la mandes por un grupo).
2. Pídele que la cambie en **Mi perfil → Cambiar contraseña** apenas entre.
3. Cierra la terminal o borra las variables (`Remove-Item Env:ConnectionStrings__Default`), para que la cadena de conexión no quede cargada.

> En desarrollo funciona igual, contra la base SQLite local: `dotnet run --project src/ControlGastos.Web --launch-profile http -- admin usuarios`.

Implementación: [ComandosAdmin.cs](../src/ControlGastos.Web/Infrastructure/ComandosAdmin.cs). La temporal sale del generador criptográfico (`RandomNumberGenerator`) y el cambio pasa por `ResetPasswordAsync`, que aplica la política de contraseñas y renueva el security stamp.

## Respaldos

Hay dos capas, las dos gratuitas:

1. **Respaldo propio de cada usuario** (en la app): *Mi perfil → Descargar respaldo* baja un `.json` con todos tus datos:
   - Incluye gastos (con su plan de cuotas), gastos fijos, tarjetas, categorías, presupuestos y ajustes manuales de cotización.
   - No incluye la contraseña ni datos de otros usuarios.
   - Conviene descargarlo una vez por mes, o antes de una importación grande, y guardarlo en tu Drive.
2. **Historial de Neon**: Neon permite restaurar la base a un momento anterior dentro de su ventana de historial. En el plan gratuito esa ventana es corta (revisar el valor actual en Neon). Sirve para un error reciente, no como archivo de largo plazo.

### Restaurar

*Mi perfil → Restaurar desde un archivo*:
- Pide tu contraseña y una confirmación explícita.
- **Reemplaza todos tus datos** por los del archivo. No toca los de otros usuarios.
- El archivo se valida completo antes de cambiar nada. Si tiene un error (otra app, otra versión, campos de más, referencias rotas, montos inválidos, colores o íconos fuera de formato), no se modifica nada y se explica el problema.
- Se guarda todo en **una transacción**: o se restaura completo o no se restaura nada.
- Se puede restaurar en producción un respaldo descargado de la versión local, y al revés: las fechas se normalizan a UTC.
- Las cuotas se recalculan a partir de cada gasto. Los gastos fijos conservan hasta qué mes se generaron, así que no se duplican meses.
- Límite: 4 MB por archivo (Vercel acepta hasta unos 4,5 MB por request). Equivale a varios miles de gastos.

Formato: JSON con `app: "Mis·Gastos"` y `version: 1`. Los ids del archivo son referencias internas y al restaurar se crean filas nuevas. Detalle en [RespaldoService.cs](../src/ControlGastos.Web/Services/RespaldoService.cs).

## Cotizaciones del dólar

- Se completan solas desde el **BCU vía [Frankfurter](https://frankfurter.dev/providers/bcu/)**, una API gratuita y sin clave. Se usa el promedio de cada mes.
- Cuándo se consulta la API:
  - Al abrir *Tipo de cambio* (últimos 12 meses) o *Reportes* en la vista "Todo en pesos" (el período del reporte).
  - Un mes cerrado se consulta **una sola vez** y queda guardado como definitivo.
  - El mes en curso se actualiza como mucho una vez por día. *Actualizar desde el BCU* fuerza la consulta.
- Si la API no responde, se sigue con lo guardado y la página no se traba (timeout de 6 segundos).
- Las cotizaciones del BCU son datos públicos, compartidos por todos los usuarios (tabla `CotizacionesBcu`). Los **ajustes manuales** son de cada usuario y mandan sobre el BCU.
- Para apagar la consulta automática: `Cotizaciones__Habilitado=false`.

## App instalable

- La app se puede instalar en el celular o la computadora (*Mi perfil → Mis·Gastos en tu celular*).
- El service worker ([sw.js](../src/ControlGastos.Web/wwwroot/sw.js)) **no guarda páginas ni datos**: solo estilos, scripts e íconos. Sin conexión muestra un aviso.
- Si cambias `site.css` o `site.js` y quieres que se actualicen al instante en las apps instaladas, sube el número de `VERSION` en `sw.js`. Si no, se actualizan en la siguiente visita.
- Los íconos se generan con `python tools/generar_iconos.py`, sin dependencias.
