# 07 · Hoja de ruta

## ✅ Etapa 0 · Base
- Solución `.sln` con el proyecto web y el de tests.
- EF Core con SQLite (desarrollo) y PostgreSQL (producción), con migraciones separadas.
- Identity, cookie de autenticación, Data Protection guardado en la base.
- Diseño heredado de gastos-equipos (layout, login, KPIs, tablas, toasts).
- Dockerfile y `render.yaml`.

## ✅ Etapa 1 · MVP
- Registro, login, logout y cambio de contraseña, con bloqueo por intentos y límite por IP.
- Rutas privadas por defecto y aislamiento total por usuario.
- Tarjetas: alta, edición, activar o desactivar, y borrado (solo sin gastos).
- Categorías: 14 por defecto, más alta, edición y borrado.
- Gastos: alta, edición y borrado; contado o en cuotas, incluidas compras ya empezadas; UYU y USD.
- Vista mensual con navegador de meses, filtros (contado o cuotas), búsqueda y totales por moneda.
- Panel con totales del mes, variación contra el mes anterior, comprometido a futuro, proyección de 6 meses, totales por categoría y tarjeta, y últimos gastos.
- 48 tests.

## ✅ Etapa 2 · Uso rápido
- **Gastos fijos** (`/fijos`):
  - Alta, edición, pausa o reanudación y borrado.
  - Se cargan solos cada mes al entrar a la app, sin job.
  - Los meses futuros aparecen como previstos y en la proyección.
  - Se puede ajustar o borrar el gasto de un mes puntual.
- "Préstamo moto" y "Facultad" pasaron a ser fijos en los datos de ejemplo.
- **Día de cierre** en tarjetas de crédito:
  - Las compras posteriores al cierre van al estado de cuenta siguiente.
  - El formulario avisa en qué estado de cuenta entra cada compra.
- **Carga rápida**:
  - Botón flotante en el celular.
  - Monto primero, con teclado numérico.
  - Autocompletado de descripciones con categoría, tarjeta y moneda.
  - "Guardar y cargar otro", que conserva la tarjeta y la fecha.
- **Filtros** por tarjeta, categoría y tipo (contado, cuotas, fijos) en la vista del mes, con total recalculado.
- **Cuotas pendientes** (`/gastos/cuotas`): planes activos agrupados por mes de fin, saldo por moneda y cuánto se libera cada mes.
- 71 tests.

## ✅ Etapa 3 · Dashboards
- **Reportes** (`/reportes`) con Chart.js 4.4.1 (jsDelivr + SRI):
  - Gasto por mes en columnas apiladas por tipo (contado, cuotas, fijos), con 3 meses de proyección.
  - Barras por categoría (las 7 principales y "Otras") y por medio de pago.
  - Indicadores: total del período, promedio mensual, mes más alto y categoría principal.
  - Períodos: 6 o 12 meses, este año, año anterior o a medida (hasta 36 meses).
  - Vista en pesos, en dólares o todo en pesos.
  - Tabla de datos en cada gráfico.
- **Exportar CSV** del período (en Reportes) o del mes (en Gastos del mes).

## ✅ Etapa 4 · Comodidad
- **Importar estado de cuenta** (`/importar`):
  - Se pega el texto y aparece una vista previa editable.
  - Detecta duplicados (también entre estados de cuenta consecutivos) y omite pagos y líneas que no son movimientos.
  - Guarda todo en una sola transacción.
- **Reglas "comercio → categoría"** aprendidas del historial, más reglas de partida para comercios comunes de Uruguay.
- **Tipo de cambio** por mes (`/tipo-cambio`) y vista "Todo en pesos" en Reportes, con aviso de meses sin cotización.
- **Presupuestos** por categoría y moneda (`/presupuestos`), con alerta visual al 80 % y al pasarse, también en el inicio.
- 120 tests (125 después de la revisión de seguridad).

## ✅ Etapa 5 · Publicación y respaldos (alcance acordado: uso personal y de pocas personas)
- [x] Revisión de seguridad previa al despliegue (ver [03-seguridad.md](03-seguridad.md#revisión-previa-al-despliegue-octubre-2026)): ReDoS corregido, registro restringido por lista de correos, hash más fuerte, sesiones revalidadas cada 5 minutos.
- [x] Soporte de la variable `PORT` para Vercel.
- [x] `Dockerfile.vercel`.
- [ ] Primer deploy en Vercel con Neon (lo hace el usuario; guía en [05-despliegue.md](05-despliegue.md)).
- [x] **Respaldos**: descarga de los datos propios desde el perfil y restauración validada, en una transacción.
- [x] **Cotización automática del dólar** (BCU vía Frankfurter, sin clave), con ajuste manual opcional.
- [x] Comandos de administración: `usuarios`, `reset-password`, `desbloquear`.
- [x] **App instalable** (PWA): manifest, íconos, service worker que no guarda datos, página sin conexión.
- 154 tests.
- Fuera de alcance por decisión: verificación en dos pasos y confirmación por email.

## Ideas para después
- Registrar los pagos de la tarjeta para conciliar lo pagado contra lo gastado.
- Importar desde PDF o desde el CSV del banco, además de texto pegado.
- Modo oscuro (los tokens de color ya están centralizados en `:root`).

## Deuda técnica conocida
- Probar el `Dockerfile` y las migraciones de PostgreSQL en un entorno con Docker y Postgres.
- Las sumas se hacen en memoria por compatibilidad con SQLite (ver [01-arquitectura.md](01-arquitectura.md)). Si los volúmenes crecen, moverlas a SQL en Postgres.
- Duplicados en la importación: solo se comparan gastos que tienen comercio. Un gasto cargado a mano sin comercio no se detecta como duplicado.
- Si se pega un texto que mezcla dos estados de cuenta, todas las líneas se ubican en un solo mes (el que se elija).
