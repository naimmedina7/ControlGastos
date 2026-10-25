# 06 · Pruebas

```bash
dotnet test
```

210 tests con xUnit, en [tests/ControlGastos.Tests](../tests/ControlGastos.Tests). Corren en unos segundos y no necesitan ninguna base instalada: usan SQLite en memoria o en un archivo temporal.

## Qué cubren

### `PlanDeCuotasTests`: reglas puras de cuotas
- Primera cuota de una compra ya empezada ("5/6" en octubre → junio).
- Una cuota por mes, numeradas, con el mismo monto; total = cuota × cantidad.
- Planes que cruzan el cambio de año ("2/12" en octubre → septiembre 2026 a agosto 2027).
- Número de cuota en un mes dado: 0 fuera del plan.
- Contado = 1 cuota en su mes.
- Rechazo de datos inválidos (monto ≤ 0, 0 o más de 72 cuotas, cuota actual fuera de rango).

### `AislamientoTests`: el DbContext con dos usuarios reales
- Las filas nuevas toman como dueño al usuario actual (gasto y cuotas).
- Otro usuario no ve gastos, cuotas ni tarjetas ajenas, ni siquiera en el resumen del mes.
- Sin usuario no se ve nada.
- Otro usuario no puede editar ni borrar (el servicio responde "no encontrado").
- No se puede usar la tarjeta de otro usuario en un gasto.
- Adjuntar a mano una fila ajena y guardarla lanza una excepción.
- No se puede crear un dato a nombre de otro.
- Editar regenera las cuotas.
- El resumen separa pesos y dólares; la proyección suma bien por mes.

### `RecurrentesTests`: gastos fijos, día de cierre y planes activos
- Con un inicio en el pasado, genera de una vez hasta el mes actual.
- Es idempotente y avanza mes a mes.
- Un mes borrado no vuelve a aparecer.
- Respeta el mes final.
- En pausa no genera; al reanudar sigue desde el mes en curso, sin los meses de pausa.
- El día 31 se acota al largo del mes (28/2).
- Editar aplica a los próximos meses sin tocar los ya cargados.
- Eliminar el fijo conserva los gastos generados.
- Previstos y proyección incluyen los meses futuros.
- Otro usuario no ve ni genera fijos ajenos.
- Valida que el mes final no sea anterior al inicial.
- Mes de facturación con cierre: el día del cierre todavía entra; después pasa al mes siguiente; cierre 31 en febrero.
- Una compra posterior al cierre empieza el mes siguiente; en débito el cierre se ignora.
- Los planes activos calculan cuotas pagadas, cuántas faltan y el saldo.

### `EstadoDeCuentaTests`: parser del estado de cuenta (puro)
- **El estado de cuenta real de octubre 2026**: 23 líneas → 21 gastos y 2 pagos, sin líneas no reconocidas. Suma **$ 13.933,48 y US$ 37,44**.
- Lee comercio, fecha, moneda, monto y "n/N".
- Funciona con espacios en lugar de tabuladores y sin la máscara de la tarjeta.
- **Formato con la fecha primero** (otro banco: `17/10/2026  •4822  MERPAGO*GER73XX  $  1.300,00  1/10`): pesos y dólares, con y sin cuota, con y sin tarjeta, año corto; pagos y cuotas fuera de rango igual que en el otro formato; los dos formatos mezclados en un mismo texto.
- Los recibos de pago y los montos negativos no son gastos.
- Encabezados, fechas imposibles, cuotas fuera de rango y totales quedan como no reconocidos.
- Montos en formato uruguayo (`1.830,00`, `-15.189,15`, `1830.00`).
- Descripción sugerida a partir del comercio.
- Normalización de comercios.
- Límite de líneas.

### `Etapa4Tests`: importación, reportes, tipo de cambio, presupuestos y CSV
- **Importar el estado de cuenta real deja octubre en $ 13.933,48 y US$ 37,44**, con Nueva Visión desde junio.
- Categorías propuestas por reglas (Delivery, Suscripciones, Servicios, Transporte, Compras, Entretenimiento, Salud, Trabajo).
- Volver a importar lo mismo: todo duplicado y desmarcado.
- El estado de cuenta de noviembre no duplica las cuotas de planes ya cargados, y sí detecta las compras nuevas.
- Aprende la descripción y la categoría que el usuario corrigió.
- El mes del estado de cuenta manda sobre el día de cierre.
- Si una línea falla, no se importa ninguna (transacción).
- El historial y los duplicados son por usuario.
- El reporte separa contado, cuotas y fijos; proyecta el futuro con cuotas y fijos previstos; total y promedio solo con meses transcurridos.
- Vista en dólares y "todo en pesos": sin cotización avisa el mes que falta; con una cotización anterior la usa.
- La tabla de cotizaciones usa la última anterior.
- Las sumas por categoría y por medio de pago coinciden con el total.
- Otro usuario ve su reporte vacío.
- Presupuestos: avance y nivel (cerca, excedido, ok); guardar dos veces actualiza sin duplicar; no se puede presupuestar una categoría ajena.
- CSV: escapado, protección contra inyección de fórmulas, una fila por cuota, coma decimal y BOM.

### `Etapa4HttpTests`: por HTTP
- Importar: analizar, confirmar con el formulario tal como lo envía el navegador, ignorar un monto inyectado, y que la segunda vez aparezca "Ya cargado".
- El CSV solo trae los gastos del usuario y neutraliza `=…`.
- `/reportes` carga Chart.js con SRI y no tiene scripts inline.
- Tipo de cambio y presupuestos son por usuario (404 al borrar los de otro; no se puede usar una categoría ajena).

### `SeguridadHttpTests`: la app completa por HTTP (`WebApplicationFactory`)
- Las rutas públicas responden sin sesión; las privadas redirigen al login.
- Con sesión, todas las rutas privadas responden 200.
- La contraseña se guarda con hash (formato v3 de Identity) y no aparece en texto plano.
- La cookie es `HttpOnly`, `Secure` y `SameSite=Lax`.
- El login funciona y el logout cierra la sesión.
- El mensaje de error del login es el mismo exista o no el correo.
- La cuenta se bloquea después de 5 intentos fallidos.
- `ReturnUrl` externo se ignora (sin open redirect).
- Se rechazan contraseñas débiles.
- Un POST sin token antiforgery responde 400.
- Las respuestas incluyen los encabezados de seguridad.
- Un usuario no puede ver, editar ni borrar el gasto de otro (404).
- Cada cuenta nueva recibe sus categorías.
- Un gasto en cuotas aparece en todos sus meses y en ninguno fuera del plan.
- Las rutas nuevas (`/fijos`, `/gastos/cuotas`) son privadas.
- Un usuario no puede ver, pausar, editar ni borrar el gasto fijo de otro, y el fijo se carga solo en los meses de su dueño.
- "Guardar y cargar otro" vuelve al formulario con la misma tarjeta y fecha, y sugiere la descripción usada.
- Una tarjeta que usa un gasto fijo no se borra; se avisa en lugar de dar error 500.

### `SeguridadExtraTests` y `RegistroRestringidoTests`: revisión previa al despliegue
- El parser no se traba con líneas hostiles (antes, más de 70 s; ahora, milisegundos) y un texto máximo se procesa rápido.
- Solo los correos de `App:CorreosPermitidos` pueden registrarse, sin distinguir mayúsculas.
- El hash de la contraseña usa 210.000 iteraciones.
- Las respuestas incluyen `Cross-Origin-Opener-Policy`.

### `Etapa5Tests` y `Etapa5HttpTests`: cotizaciones, respaldo, administración y app instalable
- **Cotizaciones** (con una API simulada):
  - Completa los meses que faltan con una sola consulta; los meses pasados quedan definitivos y el actual no.
  - No vuelve a consultar lo que ya tiene; "forzar" sí consulta. El mes en curso se actualiza al día siguiente y se cierra al terminar el mes.
  - Si la API falla o devuelve basura, sigue con lo guardado.
  - Descarta valores absurdos. Apagada, no consulta.
  - El ajuste manual manda sobre el BCU.
  - "Todo en pesos" funciona sin cargar nada a mano.
- **Respaldo**:
  - Exportar y restaurar en otra cuenta reproduce todo: mismos totales, plan de cuotas, día de cierre, presupuestos y cotizaciones, sin duplicar meses de gastos fijos.
  - No toca los datos de otra cuenta.
  - Siete tipos de archivo inválido se rechazan sin cambiar nada.
  - Sin usuario no restaura.
  - Por HTTP: la descarga es privada y solo trae datos propios (sin hash de contraseña); restaurar exige contraseña y confirmación.
- **Administración**:
  - La contraseña temporal es aleatoria y cumple la política.
  - `reset-password` permite entrar con la temporal y deja de aceptar la vieja.
  - `desbloquear` quita el bloqueo; `usuarios` lista las cuentas; un correo inexistente devuelve error.
- **App instalable**:
  - El manifest, el service worker, la página sin conexión y los íconos son públicos y tienen el tipo de contenido correcto.
  - Las páginas enlazan el manifest.
  - El service worker solo guarda estáticos.

### `ConexionTests` y `FechasUtcTests`: problemas del primer deploy
- La conexión se lee de `DATABASE_URL` con el formato de Neon, de los nombres alternativos de la integración (`POSTGRES_URL`, *unpooled*, con prefijo) o de `ConnectionStrings__Default` como URL. Si falta, el error lista solo los nombres de las variables.
- Todas las fechas se guardan en UTC: el modelo de PostgreSQL aplica el conversor a cada `DateTime`, y un respaldo con fechas sin zona (exportado desde SQLite) se restaura bien.

### `DisenoTests`: rediseño visual
- Pantalla de acceso: vista previa para compartir el link (Open Graph), título sin repetir, `noindex`, sin el nombre del stack ni datos reales, crédito con enlace a fourvexa.com.
- Estilos: ningún texto menor a 12 px, tokens de color del sistema, paleta de gráficos sin cambios, sin degradados en la tarjeta principal, fuente alojada y precargada.
- Ícono de categoría: siempre contrasta con su fondo (blanco u oscuro según el color).
- Avance de cuotas por segmentos; "Más detalles" del gasto plegado y abierto si hay errores; panel en bento con escalera y estados vacíos.
- La portada es el login y entra por `/cuenta/login`; "¿Olvidaste tu contraseña?" solo texto; requisitos de la contraseña en el registro.

### `IngresosTests`: ingresos, cuánto te queda y gráfica circular
- Alta, edición y borrado de un ingreso; validación de descripción y monto.
- Aislamiento: otro usuario no ve, no edita y no borra tus ingresos (404). `/ingresos` es privada.
- El inicio muestra cuánto te queda (40.000 − 20.000 = 20.000) y avisa si gastaste más de lo que entró. Sin ingresos en dólares no avisa nada en dólares. Pesos y dólares no se mezclan.
- El respaldo (versión 2) incluye los ingresos y los restaura; un respaldo de la versión 1 se sigue pudiendo leer.
- La dona tiene como mucho 6 porciones (resto en "Otras"); en Gastos del mes lleva nombre y % de cada categoría para el tooltip y la leyenda; también está en Reportes.

### `IndicadorCargaTests`
- Las páginas públicas y privadas incluyen la barra de carga y el aviso accesible.
- Los enlaces de descarga (respaldo, CSV) no activan la barra.
- El script bloquea el segundo envío sin deshabilitar el botón.

## Cómo agregar un test HTTP

```csharp
public class MiTest(AppFactory factory) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Algo()
    {
        var c = factory.Cliente();               // https://localhost, sin seguir redirecciones
        // registrar, obtener el token antiforgery del formulario y hacer POST…
    }
}
```

`SeguridadHttpTests` ya trae los helpers `RegistrarAsync()` (crea una cuenta e inicia sesión) y `PostFormAsync()` (agrega el token antiforgery).

## Verificación manual hecha (etapa 1)

Con la cuenta de prueba en el navegador:
- Octubre 2026: **$ 27.933,48** (los $ 13.933,48 de la tarjeta más la facultad y el préstamo) y **US$ 37,44**. Coinciden con el estado de cuenta.
- Alta de un gasto en 12 cuotas con "cuota 3 este mes": el resumen en vivo y el guardado dan agosto 2026 a julio 2027.
- Edición a 6 cuotas: se regeneran y la última cae en marzo 2027.
- La eliminación borra el gasto y sus cuotas.
- A 375 px de ancho no hay scroll horizontal.
- La consola no muestra errores de CSP.

## Verificación manual hecha (etapa 2)

- Migración de una base de la etapa 1 con datos: se conservaron los 23 gastos y el total ($ 27.933,48), y la sesión siguió abierta.
- Cuenta nueva: préstamo y facultad aparecen como fijos activos ($ 14.000 por mes) y se cargaron en octubre.
- Noviembre muestra los dos fijos como "previstos" sin sumarlos al total.
- Filtro por categoría "Compras" en noviembre: 5 filas y total recalculado de $ 6.093,13 (coincide con la suma).
- Visa con cierre el 25:
  - Una compra del 28/10 muestra el aviso "pasa al estado de cuenta de noviembre" y queda en noviembre.
  - En la tarjeta de débito, el día de cierre se ignora.
- Al escribir "Tiendamia", el formulario completa la categoría (Compras) y la tarjeta.
- "Guardar y cargar otro" vuelve con la tarjeta y la fecha anteriores.
- Cuotas pendientes agrupa los planes por mes de fin y muestra cuánto se libera cada mes.
- A 375 px el botón flotante se ve y no hay scroll horizontal.
- Bugs encontrados y corregidos:
  - `activos@if` en la vista de cuotas se imprimía como texto.
  - Borrar una tarjeta usada solo por un gasto fijo daba error 500.

## Verificación manual hecha (etapas 3 y 4)

- La migración `Etapa4TipoCambioPresupuestos` se aplicó sobre la base de la etapa 2 con datos.
- **Reportes**:
  - Columnas apiladas por tipo, con los 3 meses futuros en tono más claro.
  - Barras por categoría y por medio de pago con el valor en la punta.
  - Sin errores de CSP ni de SRI en la consola.
- **Importar** (texto mezclado de octubre y noviembre, con un "TOTAL A PAGAR"):
  - Propuso "PedidosYa · hamburguesa" en Delivery a partir del historial.
  - Marcó la cuota 6/6 de Nueva Visión como ya cargada.
  - Omitió los 2 recibos de pago y la línea de total.
- **Presupuestos**:
  - Compras al 130 % (excedido, rojo con ícono), Delivery al 98 % (cerca, ámbar) y Educación al 75 % (ok).
  - Se ven en el inicio y en `/presupuestos`.
- **Todo en pesos** con cotización de junio a $ 40,10: total $ 68.368,00, que coincide con el cálculo a mano. Avisó que faltaba la cotización de mayo (cuota de Thot en USD).
- Celular (375 px): sin scroll horizontal; el selector de período se desplaza dentro de su tarjeta.
- Correcciones durante la verificación:
  - El promedio mensual dividía por meses vacíos; ahora cuenta desde el primer mes con gastos.
  - Los selectores de mes cortaban el texto.
  - Los encabezados de fila de las tablas no tenían margen.
  - Las devoluciones (monto negativo) se mostraban como "pago de la tarjeta".

## Verificación manual hecha (etapa 5)

- La migración `Etapa5CotizacionesBcu` se aplicó sobre la base existente.
- **Tipo de cambio con la API real**:
  - Completó noviembre 2025 a octubre 2026 con el promedio del BCU (octubre parcial: $ 40,18).
  - El ajuste manual de junio ($ 40,10) se usa en lugar del BCU ($ 40,27).
- **Reportes en "Todo en pesos"**: ya no falta ninguna cotización (antes faltaba mayo); total $ 69.259,02.
- **Respaldo**:
  - Descarga de 12 KB (24 gastos, 2 gastos fijos) con `Content-Disposition: attachment`.
  - Restaurado sobre la misma cuenta, octubre sigue en $ 27.933,48 y US$ 37,44, con 23 filas.
- **Consola**: `admin usuarios` y la ayuda de `admin` funcionan.
- **App instalable**: el service worker queda registrado y el manifest enlazado; la consola no muestra errores.
- Correcciones durante la verificación:
  - En desarrollo, la variable `PORT` hacía escuchar en `0.0.0.0` (toda la red local). Ahora solo se usa fuera de desarrollo.
  - La hora de "Última consulta" pasó a mostrarse en la zona de la app (en Vercel el servidor está en UTC).

## Verificación manual hecha (indicador de carga)

- **Nuevo gasto**: al enviar, el botón cambia el ícono por la ruedita y queda ocupado (`aria-busy`). Un segundo envío inmediato se bloquea y queda **un solo gasto**.
- **"Guardar y cargar otro"** sigue funcionando: vuelve a `nuevo?tarjeta=…&fecha=…`.
- **Eliminar**: el ícono de la papelera pasa a ruedita.
- **Navegación**: la barra no aparece a los 100 ms y sí a los 350 ms, con "Cargando…" para lectores de pantalla. El enlace de descarga del CSV no la activa.
- Con "reducir movimiento" activado en el sistema, la barra queda fija y la ruedita gira lento.

