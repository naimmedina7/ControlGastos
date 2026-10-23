# 08 · Decisiones

Registro breve de las decisiones de diseño y de por qué se tomaron.

### D1 · .NET MVC en lugar de un frontend separado
Ya existía gastos-equipos (ASP.NET Core MVC) con el diseño terminado, y es el stack que el equipo conoce. Una app server-rendered implica un solo deploy, no tener build de frontend y usar cookies seguras sin exponer tokens a JavaScript. **Alternativa descartada**: Next.js + Supabase, que obliga a rehacer todo en otro lenguaje.

### D2 · Cookie de autenticación en lugar de JWT
Ver [03-seguridad.md](03-seguridad.md#token-de-sesión-cookie-de-autenticación). Si en el futuro hay una API para una app móvil, se agrega JWT solo para `/api`.

### D3 · Identity en lugar de una autenticación propia
El hash de contraseñas, el bloqueo por intentos, el security stamp y los tokens de recuperación ya están hechos y auditados. Escribirlos a mano es el lugar donde suelen aparecer las vulnerabilidades.

### D4 · Aislamiento con filtro global de EF y control al guardar
Filtrar a mano en cada consulta se termina olvidando en algún lugar. El filtro global lo hace imposible de olvidar, y el control en `SaveChanges` cubre el caso de escritura. **Alternativa a futuro**: Row Level Security de PostgreSQL como tercera capa.

### D5 · SQLite en desarrollo, PostgreSQL en producción, con migraciones separadas
Esta máquina no tiene Docker ni Postgres, y SQLite permite clonar y correr al instante. El costo es generar cada migración dos veces. **Alternativa**: Postgres también en desarrollo (Docker o Neon), con un solo juego de migraciones. Es razonable pasar a eso si las diferencias entre motores empiezan a molestar.

### D6 · Una tabla de cuotas materializada
En lugar de calcular al vuelo en qué meses cae cada gasto, se guarda una fila por mes. Simplifica las consultas mensuales y los dashboards, y permite ajustar cuotas puntuales. Se regenera completa al editar el gasto. Ver [02-modelo-de-datos.md](02-modelo-de-datos.md).

### D7 · Monto de la cuota en lugar del total
El estado de cuenta muestra el monto de la cuota y el "n/N", así que se carga tal cual. El total se calcula. Para no olvidar casos, el formulario pregunta "¿qué cuota es este mes?", lo que permite cargar compras ya empezadas.

### D8 · El tipo débito o crédito es de la tarjeta, no del gasto
Una tarjeta llamada "Itaú débito" ya define el tipo. Lo que no pasa por tarjeta (efectivo o transferencia) es `TarjetaId = null`.

### D9 · No sumar monedas distintas
Sumar pesos y dólares da un número sin sentido. Hasta tener tipo de cambio (etapa 4), todo se muestra separado por moneda.

### D11 · Gastos fijos generados al entrar, sin job programado
Un job (Hangfire, cron) suma infraestructura y en el plan gratuito de Render el contenedor se duerme. Generar los meses pendientes la primera vez que se abre una pantalla de totales da el mismo resultado para un uso personal, con una consulta extra. Si algún día hace falta (por ejemplo, para enviar avisos por email), se agrega un job que llame al mismo `GenerarPendientesAsync`.

### D12 · `GeneradoHasta` en lugar de "existe el gasto de ese mes"
Si la generación preguntara "¿ya existe el gasto de este mes?", al borrar un mes este volvería a aparecer. Con `GeneradoHasta` cada mes se genera una sola vez y borrar es definitivo. El índice único (recurrente, mes) protege contra duplicados si dos requests generan al mismo tiempo.

### D13 · Los meses futuros de un fijo no se guardan
Guardarlos por adelantado obligaría a regenerarlos cada vez que se edita el fijo y los mezclaría con los gastos reales. Se muestran como "previstos" y se suman solo a la proyección.

### D14 · El "mes" de un gasto es el del estado de cuenta
Con día de cierre, lo útil es saber cuánto vas a pagar en cada estado de cuenta, no en qué día compraste. La fecha real se conserva en `Fecha`; `PrimerPeriodo` usa el mes de facturación. Cambiar el cierre no recalcula lo ya cargado, para que no se muevan gastos de mes sin que te des cuenta.

### D15 · Importar pegando texto, no subiendo un PDF
Copiar los movimientos de la web del banco o del PDF ya da texto con columnas. Interpretar texto es simple, se puede testear con el estado de cuenta real y no requiere procesar archivos subidos, que suman superficie de ataque. Si algún día hace falta, un importador de PDF o CSV puede producir el mismo `LineaEstadoCuenta`.

### D16 · Al confirmar, el servidor vuelve a interpretar el texto
La vista previa podría enviar cada campo (monto, fecha, moneda) en inputs ocultos, pero cualquiera puede alterarlos. Mandar el texto y solo las decisiones del usuario por número de línea garantiza que lo importado es exactamente lo que dice el estado de cuenta.

### D17 · Las reglas de categoría se aprenden del historial, sin tabla de reglas
El último gasto con el mismo comercio ya dice qué descripción y categoría prefiere el usuario. Una tabla de reglas aparte duplicaría esa información y habría que mantenerla. Las reglas de partida (en código) solo se usan cuando el comercio nunca se vio.

### D18 · Cotización por mes cargada por el usuario
El usuario sabe a qué cotización le cobró la tarjeta. Una cotización por mes alcanza para ver tendencias "en pesos" y evita depender de una API externa. Traerla del BCU queda como idea para después.

### D19 · Chart.js desde CDN con SRI, solo en Reportes
Es una sola página la que lo necesita: no tiene sentido cargarlo en todas. Con versión fija y SRI, el CDN no puede cambiar el código que se ejecuta. El gráfico del panel sigue en CSS, sin JS, porque es liviano y se ve al instante.

### D20 · Promedio desde el primer mes con gastos
Si el período elegido empieza antes de que haya datos, dividir por todos los meses daría un promedio artificialmente bajo. Se cuentan los meses transcurridos desde el primero que tiene gastos.

### D21 · Vercel con contenedor en lugar de reescribir la app
Vercel no corre .NET de forma nativa, pero su Container Runtime (Beta, en todos los planes) ejecuta el mismo Dockerfile. La app ya estaba preparada para ese modelo: Postgres en lugar de disco, claves de sesión en la base, sin procesos en segundo plano. Reescribirla en Node o Next.js para usar el runtime nativo no se justifica.

### D22 · Registro por lista de correos en lugar de verificación en dos pasos
Es una app para una persona y su entorno. Restringir quién puede crear cuenta elimina el riesgo principal (desconocidos usando la app) sin agregar proveedores de email ni pasos extra al iniciar sesión.

### D23 · Cotizaciones del BCU en una tabla compartida
La cotización del dólar es la misma para todos y es pública. Guardarla una sola vez evita repetir consultas por usuario. Es la única tabla sin `UserId`, y no tiene datos personales. Lo personal (el ajuste manual) sigue en `TipoCambio`, con filtro por usuario.

### D24 · Frankfurter con proveedor BCU, y no el SOAP del BCU
El dato es el mismo (fuente BCU), pero Frankfurter lo da en JSON, sin clave, con promedios mensuales en una sola consulta. Si un día deja de estar disponible, se cambia `Cotizaciones:Url` o se reemplaza el `CotizacionService`, sin tocar el resto: la app sigue funcionando con lo guardado y los ajustes manuales.

### D25 · Respaldo por usuario en JSON, y no un dump de la base
Cada persona puede llevarse y restaurar sus propios datos sin depender de nadie, y sin acceso a la base. Un `pg_dump` mezclaría datos de todos y exigiría credenciales de la base. El historial de Neon cubre el desastre general.

### D26 · El service worker no guarda páginas
Una app instalable suele funcionar sin conexión guardando las páginas, pero aquí las páginas son tus finanzas. En un celular compartido o perdido no deben quedar en caché. Sin conexión se muestra un aviso: se pierde comodidad, pero no se exponen datos.

### D27 · Contraseñas olvidadas por consola, y no por email
Para pocas personas, que el administrador genere una temporal con un comando es más simple y más seguro que configurar un proveedor de email (dominio, SPF/DKIM, costo) solo para eso.

### D10 · Lo que no se reutilizó de gastos-equipos
El singleton `Sistema` en memoria, las contraseñas en texto plano, los ids estáticos, el uso de `double` para montos, los descuentos y recargos del obligatorio, y los roles empleado y gerente. Sí se reutilizó: `site.css`, el layout, los toasts, el logo, la lógica de `Fmt` (adaptada a `decimal` y moneda) y la idea de "cuota n de N" por período.
