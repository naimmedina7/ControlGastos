# 09 · Guía de uso

Cómo usar Mis·Gastos día a día, con ejemplos de un estado de cuenta real.

## 1. Primera vez

1. **Crear la cuenta** en `/cuenta/registro`. La contraseña necesita 8 caracteres o más, con al menos un número y una letra minúscula.
2. **Crear tus tarjetas** (Configuración → Tarjetas):
   - Un nombre que reconozcas, por ejemplo "Visa crédito" o "Itaú débito". **Nunca el número de la tarjeta.**
   - El tipo: crédito o débito.
   - Si es de crédito, el **día de cierre** del estado de cuenta (por ejemplo, 25).
3. Revisar las **categorías**. Vienen 14 creadas, que puedes renombrar, borrar o ampliar.
4. Cargar tus **gastos fijos** (Gastos → Gastos fijos): alquiler, facultad, préstamo, suscripciones.

## 2. Cargar un gasto

Botón **Nuevo gasto** (arriba a la derecha, o el botón verde redondo en el celular):

1. **Monto** y moneda (pesos o dólares).
2. **Fecha** de la compra.
3. **Descripción**. Si ya la usaste antes, el formulario la sugiere y completa la categoría, la tarjeta y la moneda de la última vez.
4. **Medio de pago**: una tarjeta, o "Efectivo / transferencia".
5. Categoría y comercio, opcionales.

El panel de la derecha muestra el resumen y, si la tarjeta tiene día de cierre, **en qué estado de cuenta entra la compra**.

> Para cargar varios gastos seguidos, usa **Guardar y cargar otro**: vuelve al formulario con la misma tarjeta y la misma fecha. Para un estado de cuenta entero, es más rápido **importarlo** (sección siguiente).

## 2 bis. Importar el estado de cuenta

Herramientas → **Importar estado de cuenta**:

1. Copia los movimientos desde la web del banco o desde el PDF y pégalos. No hace falta limpiar nada: los encabezados, los totales y los "RECIBO DE PAGO" se detectan y se omiten.
2. Elige la **tarjeta**. El mes del estado de cuenta se deduce solo, pero puedes cambiarlo.
3. Presiona **Analizar** y revisa la vista previa:
   - Cada línea trae una descripción y una categoría propuestas. Si ya importaste ese comercio antes, se repite lo que elegiste la última vez.
   - Las líneas **Ya cargado** (por ejemplo, la cuota 6/6 de un plan que ya cargaste el mes pasado) quedan desmarcadas.
   - Abajo se ve el total de lo seleccionado, para compararlo con el del estado de cuenta.
4. **Importar seleccionados**. Se guardan todas las líneas o ninguna.

Lo que corrijas en la vista previa (descripción, categoría) se recuerda para el mes siguiente.

## 3. Compras en cuotas

Activa **Es una compra en cuotas** y carga el monto de **una cuota**, no el total.

| En el estado de cuenta | Cómo cargarlo |
|---|---|
| `LA CUEVA MUEBLES 21/10/26 998,33 1/6` | Monto 998,33 · fecha 21/10/2026 · 6 cuotas · cuota de este mes **1** |
| `NUEVA VISION 06/10/26 1.830,00 5/6` | Monto 1.830 · fecha 06/10/2026 · 6 cuotas · cuota de este mes **5** |

La app calcula sola en qué mes empezó y en cuál termina (Nueva Visión: de junio a noviembre 2026), y suma la cuota en cada mes.

Para ver cuánto te falta pagar de cada compra y en qué mes se libera cada cuota, entra a **Cuotas pendientes**.

## 4. Gastos fijos

Lo que pagas **todos los meses** con un monto conocido.

- Se cargan **solos** en cada mes: la primera vez que abres la app en un mes nuevo, ya están.
- En los meses que vienen aparecen como **previstos**: se ven, pero todavía no suman.
- **Si un mes vino distinto** (por ejemplo, la facultad aumentó solo ese mes), edita el gasto de ese mes desde *Gastos del mes*.
- **Si cambia para siempre**, edita el gasto fijo: aplica desde el próximo mes que se cargue.
- **Pausar** sirve si dejas de pagar unos meses. Al reanudar, sigue desde el mes en curso, sin cargar los meses de pausa.
- **Si borras** el gasto de un mes, no vuelve a aparecer. **Si borras el gasto fijo**, los meses ya cargados se conservan.

¿Fijo o en cuotas?

| Caso | Usar |
|---|---|
| Compraste algo en 12 cuotas (total conocido) | Compra en cuotas |
| Facultad, alquiler, Netflix, préstamo sin fecha de fin | Gasto fijo |
| Préstamo con cantidad de cuotas conocida | Cualquiera de los dos: como fijo con "Hasta", o como compra en cuotas |

## 5. Día de cierre

Si la Visa cierra el **25**:
- Una compra del 20/10 entra en el estado de cuenta de **octubre**.
- Una compra del 27/10 entra en el de **noviembre**, y ahí se cuenta.

Así, "Gastos del mes" de noviembre muestra lo que vas a pagar en ese estado de cuenta. Si cambias el día de cierre, aplica a lo que cargues o edites después.

## 6. Ver tus gastos

- **Inicio**:
  - Total del mes en pesos y en dólares, y la variación contra el mes anterior.
  - Lo comprometido en cuotas a futuro.
  - Los próximos 6 meses (cuotas más gastos fijos previstos).
  - Totales por categoría y por tarjeta.
- **Gastos del mes**:
  - Cambia de mes con las flechas o con el selector.
  - Filtra por tipo (contado, cuotas, fijos), tarjeta y categoría, y busca por texto.
  - El total de abajo se recalcula con lo filtrado.
- **Cuotas pendientes**: compras que todavía estás pagando, agrupadas por mes de fin.

Pesos y dólares **nunca se suman entre sí**: cada total se muestra por moneda.

## 6 bis. Reportes

Análisis → **Reportes**:
- **Período**: 6 o 12 meses (con 3 meses de proyección), este año, el año anterior, o un rango a medida.
- **Moneda**: Pesos, Dólares o **Todo en pesos**. Para esta última, carga la cotización del dólar en Herramientas → **Tipo de cambio**. Alcanza con una por mes (o una cada tanto: un mes sin cotización usa la anterior).
- **Gasto por mes**: columnas por tipo (contado, cuotas, fijos). Los meses futuros, en tono más claro, son lo que ya está comprometido.
- **Por categoría** y **por medio de pago**: dónde se va la plata y con qué la pagas.
- Cada gráfico tiene **Ver tabla de datos**, y al pasar el mouse muestra los montos.
- **Exportar CSV** descarga los movimientos del período para abrirlos en Excel o Google Sheets. En *Gastos del mes* también está el botón **CSV** del mes.

## 6 ter. Presupuestos

Análisis → **Presupuestos**: un límite mensual por categoría (por ejemplo, "Delivery y comidas: $ 4.000").
- La barra muestra cuánto llevas en el mes. Suman las compras al contado, la cuota del mes y los gastos fijos de esa categoría.
- **Cerca del límite** (desde el 80 %) se ve en ámbar con un aviso; **excedido**, en rojo.
- El inicio muestra los presupuestos del mes, empezando por los más comprometidos.

## 6 cuater. Tipo de cambio

Herramientas → **Tipo de cambio**:
- La cotización se completa sola con el **promedio mensual del BCU**. No tienes que cargar nada.
- Si tu tarjeta te cobró a otra cotización, carga un **ajuste manual** para ese mes: manda sobre el BCU. Lo puedes quitar con la ✕.
- **Actualizar desde el BCU** vuelve a consultar el mes en curso. Igual se actualiza solo una vez por día.

## 6 quinquies. Respaldo

*Mi perfil* → **Descargar respaldo**: un archivo con todos tus datos. Guárdalo en tu Drive una vez por mes.

Para volver a un respaldo: *Mi perfil* → **Restaurar desde un archivo**. Pide tu contraseña y **reemplaza todo lo que tienes** por lo del archivo. Si el archivo tiene algún problema, no se cambia nada.

## 6 sexies. La app en tu celular

*Mi perfil* → **Mis·Gastos en tu celular**:
- **Android (Chrome)**: menú ⋮ → *Instalar app*.
- **iPhone (Safari)**: Compartir → *Agregar a inicio*.

Queda como una app más, con su ícono, y abre directo en el inicio. Desde el ícono también puedes ir directo a *Nuevo gasto* (mantenlo presionado en Android). Sin conexión muestra un aviso: por privacidad, tus datos no se guardan en el teléfono.

## 7. Seguridad de tu cuenta

- Nadie más ve tus datos: cada cuenta está aislada.
- Cambia la contraseña desde **Mi perfil**: se cierran las sesiones abiertas en otros dispositivos.
- Tras 5 intentos fallidos, la cuenta se bloquea 15 minutos.
- Si olvidas la contraseña, pídele a quien administra la app una contraseña temporal (no hay recuperación por email) y cámbiala apenas entres.
- "Mantener la sesión iniciada" conviene solo en tus propios dispositivos.
