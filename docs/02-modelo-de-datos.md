# 02 · Modelo de datos

## Diagrama

```
AppUser (Identity: AspNetUsers)
 ├─< Tarjeta          (UserId, Nombre único por usuario, Tipo, Color, Activa, DiaCierre?)
 ├─< Categoria        (UserId, Nombre único por usuario, Icono, Color)
 ├─< GastoRecurrente  (UserId, Descripcion, TarjetaId?, CategoriaId?, Moneda, Monto, DiaDelMes,
 │                     Desde, Hasta?, Activo, GeneradoHasta?, Notas?)
 ├─< Gasto            (UserId, Descripcion, Comercio?, Fecha, TarjetaId?, CategoriaId?,
 │                     Moneda, MontoCuota, CantidadCuotas, PrimerPeriodo, Notas?, GastoRecurrenteId?)
 │     └─< Cuota      (UserId, GastoId, Numero, Periodo, Monto)
 ├─< TipoCambio       (UserId, Mes único por usuario, UsdEnPesos)   ← ajuste manual
 ├─< Presupuesto      (UserId, CategoriaId, Moneda, MontoMensual; único por categoría y moneda)
 ├─< Ingreso          (UserId, Descripcion, Tipo, Fecha, Moneda, Monto, Notas?)
 └─< DataProtectionKeys (claves que cifran la cookie; no son por usuario)

CotizacionBcu (Mes PK, UsdEnPesos, Definitiva, ActualizadoEn)   ← dato público, compartido
```

Todas las tablas propias tienen `UserId` con FK a `AspNetUsers` y **borrado en cascada**: si se elimina una cuenta, se van todos sus datos.

## Entidades

### Tarjeta
| Campo | Tipo | Notas |
|---|---|---|
| Nombre | texto (60) | Libre, por ejemplo "Itaú débito". Único por usuario. **No se guardan números de tarjeta.** |
| Tipo | `Debito` / `Credito` | El débito o crédito es de la tarjeta, no de cada gasto. |
| Color | `#RRGGBB` | Se valida con regex; se usa en listados y gráficos. |
| Activa | bool | Una tarjeta inactiva no se ofrece al cargar gastos, pero conserva su historial. |
| DiaCierre | int? (1–31) | Solo crédito (en débito se ignora). Ver [Mes de facturación](#mes-de-facturación-día-de-cierre). |

Una tarjeta con gastos **no se puede borrar** (FK `Restrict`): se desactiva.

### Categoria
Nombre único por usuario, ícono de Bootstrap Icons (validado con `^bi-[a-z0-9-]+$`) y color. Cada cuenta nueva recibe 14 categorías ([CategoriasPorDefecto.cs](../src/ControlGastos.Web/Domain/CategoriasPorDefecto.cs)). Al borrar una categoría, sus gastos quedan "sin categoría" (no se borran).

### Gasto
| Campo | Notas |
|---|---|
| Descripcion | Cómo lo quieres ver ("Mercado Libre · auriculares"). |
| Comercio | Opcional: el texto del estado de cuenta (`MERPAGO*SEP`). Servirá para la importación (etapa 4). |
| Fecha | Fecha de la compra o del cargo. |
| TarjetaId | `null` = efectivo o transferencia (préstamo de la moto, facultad). |
| Moneda | `UYU` / `USD` (se guarda como texto). |
| MontoCuota | Lo que se paga **cada mes**. Al contado es el monto total. |
| CantidadCuotas | 1 = contado. Máximo 72. |
| PrimerPeriodo | Primer día del mes de la cuota 1. |
| GastoRecurrenteId | Si lo generó un gasto fijo. Índice único (GastoRecurrenteId, PrimerPeriodo): un fijo genera un solo gasto por mes. |

Calculados (no se guardan): `MontoTotal = MontoCuota × CantidadCuotas`, `UltimoPeriodo`, `EnCuotas`, `EsFijo`.

### GastoRecurrente (gasto fijo)
Plantilla de un gasto que se repite todos los meses: alquiler, facultad, préstamo, suscripciones.

| Campo | Notas |
|---|---|
| Monto, Moneda | Monto mensual. |
| DiaDelMes | 1–31. En meses más cortos se usa el último día (31 → 28/2). |
| Desde, Hasta? | Primer y último mes (primer día del mes). Sin `Hasta`, no tiene fin. |
| Activo | En pausa no genera. Al reanudar se sigue desde el mes en curso: **no se cargan los meses en pausa**. |
| GeneradoHasta | Último mes ya generado. Cada mes se genera **una sola vez**: si borras el gasto de un mes, no vuelve a aparecer. |

Reglas:
- Genera un `Gasto` común (contado, `GastoRecurrenteId` = el fijo) por cada mes entre `Desde` y el mes en curso. Los meses futuros se muestran como **previstos** y se suman a la proyección, pero no se guardan.
- Editar el fijo cambia solo los meses todavía no generados. El gasto de un mes ya cargado se edita como cualquier gasto (por ejemplo, si un mes vino distinto).
- Al borrar el fijo, los gastos ya generados se conservan como gastos comunes.
- Una tarjeta usada por un fijo no se puede borrar (FK `Restrict`); se desactiva.

### TipoCambio (ajuste manual) y CotizacionBcu
- **`CotizacionBcu`**:
  - Promedio mensual del dólar según el BCU, traído automáticamente de Frankfurter.
  - Es un dato público: no tiene `UserId` ni filtro y lo comparten todos los usuarios.
  - `Definitiva` = el mes ya cerró y no se vuelve a consultar.
- **`TipoCambio`**:
  - Ajuste manual del usuario para un mes (por ejemplo, la cotización a la que le cobró la tarjeta).
  - Es por usuario y **manda sobre el BCU**.
- **Valor que se usa en cada mes**: el ajuste manual; si no hay, el BCU de ese mes; si tampoco hay, el último mes anterior que tenga alguno de los dos. Si no hay nada, los dólares de ese mes quedan fuera de "Todo en pesos" y el reporte lo avisa.

### Presupuesto
Límite mensual (`MontoMensual`) para una categoría en una moneda.
- El avance del mes suma todo lo que cae en esa categoría y moneda: contado, cuota del mes y gastos fijos.
- Niveles: **ok** (< 80 %), **cerca** (80–100 %), **excedido** (> 100 %).
- Si se borra la categoría, se borra su presupuesto (cascada).

### Ingreso
Plata que entra. Cuenta en el **mes de su fecha** (no hay día de cierre).

| Campo | Notas |
|---|---|
| Descripcion | "Sueldo de octubre", "Venta de la bici". |
| Tipo | `Sueldo`, `Venta`, `PrestamoDevuelto` (plata que prestaste y te devolvieron) u `Otro`. Se guarda como texto. |
| Fecha | Define el mes. |
| Moneda, Monto | `UYU` / `USD`; monto mayor a cero, con 2 decimales. |

**Cuánto te queda** = ingresos del mes − gastos del mes (contado, cuota del mes de cada compra y gastos fijos ya cargados), **por moneda**. Lo calcula `IngresoService.Calcular` y se muestra en el inicio y en Ingresos.

### Cuota
Una fila por cada mes en que el gasto impacta. **Se regenera completa** cada vez que el gasto se edita.

## Cómo se calculan las cuotas

El formulario pide la **fecha** y **qué cuota se paga en el mes de esa fecha** (por defecto, la 1). Así se pueden cargar compras ya empezadas tal como aparecen en el estado de cuenta:

```
NUEVA VISION   06/10/26   1.830,00   5/6
→ Fecha = 2026-10-06, CantidadCuotas = 6, CuotaActual = 5
→ PrimerPeriodo = octubre − (5 − 1) meses = junio 2026
→ Cuotas: jun, jul, ago, sep, oct, nov 2026 (1.830 cada una)
```

La lógica está aislada en [PlanDeCuotas.cs](../src/ControlGastos.Web/Domain/PlanDeCuotas.cs) y tiene sus propios tests.

## Mes de facturación (día de cierre)

En una tarjeta de crédito con `DiaCierre`, el "mes" de un gasto es el **mes del estado de cuenta** en que entra:

```
Visa, cierre el 25
  compra del 25/10 → estado de cuenta de octubre
  compra del 26/10 → estado de cuenta de noviembre
  cierre 31 en febrero → se toma el 28 (o 29)
```

- La regla está en `PlanDeCuotas.PeriodoDeFacturacion` y se aplica igual a gastos cargados a mano, a compras en cuotas (la "cuota de este mes" es la del mes de facturación) y a los gastos fijos que se pagan con esa tarjeta.
- Sin día de cierre, en débito y en efectivo, el mes es el de la fecha.
- Cambiar el día de cierre **no recalcula** los gastos ya cargados: aplica a los que se carguen o editen después.

### Por qué una tabla de cuotas y no un cálculo al vuelo

- Cada consulta de un mes es un simple `WHERE Periodo = @mes`, sin aritmética de fechas en SQL.
- Los dashboards (etapa 3) agrupan una sola tabla por período, categoría o tarjeta.
- "Comprometido a futuro" es `WHERE Periodo > @mes`.
- Permite, a futuro, ajustar una cuota puntual (por redondeo o intereses).

## Monedas

- Solo se admiten `UYU` y `USD`. Agregar otra es sumar un valor al enum `Moneda` y una opción en el formulario.
- En las pantallas de gastos los totales se muestran siempre por moneda.
- En Reportes se elige una vista: **Pesos**, **Dólares** o **Todo en pesos**. Esta última convierte cada mes con su `TipoCambio`.

## Importación del estado de cuenta

Formato que se reconoce, una línea por movimiento, con columnas separadas por tabulador o espacios:

```
**** 1012   MERPAGO*MERCADOLIBRE   Plan Pagos   05/10/26   Pesos   407,90   3/3
            NETFLIX.COM            Comun        21/10/26   Dólares 5,17
```

- **Opcionales**:
  - La máscara de la tarjeta (`**** 1012`).
  - El tipo (`Plan Pagos`, `Comun`).
  - La cuota (`n/N`).
- **Montos**: formato uruguayo (`1.830,00`) y también `1830.00`.
- **Moneda**: `Pesos`, `UYU` o `$` → UYU; `Dólares`, `USD` o `U$S` → USD.
- **Se omiten**:
  - `RECIBO DE PAGO` y otros pagos de la tarjeta.
  - Los montos negativos (devoluciones o créditos).
  - Las líneas sin fecha, moneda y monto (encabezados, totales).
- **Mes del estado de cuenta**: se deduce de la compra más reciente (respetando el día de cierre) y se puede cambiar. Todas las líneas importadas se ubican en ese mes: una "5/6" en el estado de octubre empieza en junio, sin importar la fecha ni el cierre.
- **Sugerencias**:
  - Si el comercio ya se importó antes, se repiten la descripción y la categoría que el usuario dejó la última vez.
  - Si no, se usan reglas de partida por palabra completa (PEDIDOSYA → Delivery, NETFLIX → Suscripciones, ANTEL → Servicios, etc.).
- **Duplicados**:
  - Un plan en cuotas ya cargado se reconoce por comercio, monto, cantidad de cuotas y mes de inicio. Así, el estado de cuenta del mes siguiente no lo duplica.
  - Una compra al contado se reconoce, además, por la fecha.
  - Los duplicados quedan desmarcados en la vista previa.
- **Límites**: 30.000 caracteres y 400 líneas por importación.

## Lo que todavía no está en el modelo

| Concepto | Etapa | Idea |
|---|---|---|
| Pago de tarjeta | futuro | Los "RECIBO DE PAGO" en su propia entidad, para conciliar lo pagado contra lo gastado. |

