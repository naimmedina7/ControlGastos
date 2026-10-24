# 03 · Seguridad

Resumen de cómo se protege la cuenta y los datos de cada usuario, y dónde está implementado.

## Contraseñas con hash

- Las contraseñas las maneja **ASP.NET Core Identity** (`UserManager.CreateAsync`, `ChangePasswordAsync`).
- Identity guarda solo el hash en `AspNetUsers.PasswordHash`, con formato v3: **PBKDF2 con HMAC-SHA512, salt aleatorio de 128 bits y 210.000 iteraciones** (recomendación de OWASP; configurado con `PasswordHasherOptions`). Un hash viejo con menos iteraciones se sigue aceptando y se rehashea solo en el próximo login. La contraseña en texto plano nunca se guarda ni se registra en logs.
- Política de contraseñas ([Program.cs](../src/ControlGastos.Web/Program.cs)): mínimo 8 caracteres, al menos un número y una letra minúscula.
- Tests: `La_contrasena_se_guarda_con_hash` y `El_hash_usa_210000_iteraciones`.

## Token de sesión (cookie de autenticación)

En una app MVC el "token" de las rutas privadas es la cookie `.ControlGastos.Auth`:

| Propiedad | Valor | Para qué |
|---|---|---|
| Contenido | Ticket **cifrado y firmado** con Data Protection | No se puede leer ni falsificar sin la clave del servidor. |
| `HttpOnly` | sí | JavaScript no la puede leer, así que un XSS no la roba. |
| `Secure` | siempre en producción | Solo viaja por HTTPS. |
| `SameSite` | `Lax` | No se envía en POST desde otros sitios (refuerza CSRF). |
| Duración | 8 h con renovación por uso; "Mantener sesión" la hace persistente | |
| Security stamp | revalidado cada 5 min | Al cambiar la contraseña, las sesiones de otros dispositivos se cierran en 5 minutos como máximo. |

Las **claves de Data Protection se guardan en la base** (`DataProtectionKeys`). Sin esto, cada redeploy del contenedor generaría claves nuevas y cerraría todas las sesiones.

> El log avisa *"No XML encryptor configured"*: las claves quedan sin cifrar dentro de la base. Es aceptable porque la base ya es privada y va por TLS. Si se quisiera cifrarlas, existe `ProtectKeysWithCertificate`.

**¿Por qué no JWT?** El HTML lo genera el servidor, así que la cookie es más segura y simple. Un JWT en `localStorage` queda expuesto a XSS y no se puede revocar. Si en el futuro hay una app móvil, se agrega un `/api` con JWT Bearer **además** de la cookie.

## Rutas públicas y privadas

- **Todo es privado por defecto**: hay un `AuthorizeFilter` global que exige un usuario autenticado. Una ruta nueva nace protegida.
- Solo son públicas las acciones marcadas con `[AllowAnonymous]`:

| Ruta | Controlador |
|---|---|
| `/` | `InicioController.Index` (con sesión redirige a `/panel`) |
| `/error/{code}` | `InicioController.Error` |
| `/cuenta/login` (GET/POST) | `CuentaController` |
| `/cuenta/registro` (GET/POST) | `CuentaController`, restringido a `App:CorreosPermitidos` (o cerrado del todo con `App:RegistroHabilitado=false`) |

- Privadas: `/panel`, `/gastos/**` (incluye `/gastos/cuotas`), `/fijos/**`, `/reportes/**` (incluye `/reportes/exportar`), `/importar/**`, `/tipo-cambio/**`, `/presupuestos/**`, `/tarjetas/**`, `/categorias/**`, `/cuenta/perfil`, `/cuenta/logout`.
- Sin sesión → `302` a `/cuenta/login?ReturnUrl=…`.
- Tests: `Las_rutas_privadas_redirigen_al_login_sin_sesion`, `Las_rutas_publicas_responden_sin_sesion`.

## Aislamiento de datos entre usuarios

Funciona en dos capas, en [AppDbContext.cs](../src/ControlGastos.Web/Data/AppDbContext.cs):

1. **Filtro global de consultas.** Cada entidad que implementa `IPerteneceAUsuario` (Tarjeta, Categoria, Gasto, Cuota, GastoRecurrente, TipoCambio, Presupuesto, Ingreso) tiene `HasQueryFilter(x => x.UserId == UsuarioActualId)`. El `UsuarioActualId` sale del claim `NameIdentifier` de la cookie. Toda consulta, incluidos `Include` y `Any`, lleva ese `WHERE`. Sin usuario no se ve nada.
2. **Control al guardar.** `SaveChanges` asigna el `UserId` a las filas nuevas y **lanza una excepción** si se intenta crear, modificar o borrar una fila de otro usuario, aunque alguien saltee el filtro adjuntando la entidad a mano.

Además:
- Las FK que llegan del formulario (`TarjetaId`, `CategoriaId`) se validan contra los datos del propio usuario: no se puede asociar un gasto a la tarjeta de otro.
- Pedir el id de un gasto ajeno responde **404**, igual que si no existiera, así que no se revela qué ids están ocupados.
- La generación de gastos fijos corre con el usuario de la request: solo ve y genera **sus** fijos.
- Tests:
  - `AislamientoTests`: 9 casos sobre el DbContext.
  - `Otro_usuario_no_ve_ni_genera_los_fijos_ajenos`.
  - Por HTTP: `Un_usuario_no_puede_ver_editar_ni_borrar_gastos_de_otro` y `Un_usuario_no_puede_ver_ni_modificar_gastos_fijos_de_otro`.

## Login

- **Mensaje genérico** ("Correo o contraseña incorrectos."), así no se revela si un correo está registrado.
- **Bloqueo de la cuenta**: 5 intentos fallidos la bloquean 15 minutos (lockout de Identity).
- **Límite de velocidad por IP** en login, registro y cambio de contraseña: 10 por minuto, configurable con `Seguridad:IntentosPorMinuto`. Al pasarse se responde 429.
- **Sin open redirect**: `ReturnUrl` solo se respeta si es una URL local (`Url.IsLocalUrl`).
- **El logout es POST** con antiforgery, así un enlace o una imagen no pueden cerrar la sesión.

## CSRF

`AutoValidateAntiforgeryTokenAttribute` es global: **todo POST** necesita el token que los tag helpers agregan a cada `<form>`. Sin token se responde 400. Test: `Un_POST_sin_token_antiforgery_se_rechaza`.

## Encabezados HTTP

Los agrega [EncabezadosSeguridad.cs](../src/ControlGastos.Web/Infrastructure/EncabezadosSeguridad.cs):

- `Content-Security-Policy`:
  - Scripts solo propios y de jsDelivr, sin scripts inline.
  - `frame-ancestors 'none'`, `form-action 'self'` y `object-src 'none'`.
- `X-Frame-Options: DENY` (evita clickjacking).
- `X-Content-Type-Options: nosniff`.
- `Referrer-Policy: strict-origin-when-cross-origin`.
- `Permissions-Policy`: cámara, micrófono y ubicación deshabilitados.
- `Cross-Origin-Opener-Policy: same-origin`: otra pestaña abierta desde un sitio ajeno no puede controlar la ventana de la app.
- En producción, además: **HSTS** y redirección a HTTPS.

## Otras medidas

- Razor codifica toda salida (contra XSS). Los colores que se insertan en `style` se validan como `#RRGGBB`.
- Las sugerencias del formulario de gasto y los datos de los gráficos viajan como JSON dentro de atributos HTML: Razor los codifica y el JS los lee con `JSON.parse`, nunca como código.
- Las validaciones del servidor no dependen del JS (DataAnnotations y reglas en `GastoService`).
- `appsettings.Development.json`, que tiene la cuenta de prueba, **no se publica** (`CopyToPublishDirectory=Never` y `.dockerignore`).
- Los secretos de producción (cadena de conexión) van solo en variables de entorno.
- El contenedor corre con un usuario sin privilegios (`USER $APP_UID`).
- Paquetes en su última versión de parche (10.0.13), sin vulnerabilidades conocidas (`dotnet list package --vulnerable`).

## Etapas 3 y 4

- **Chart.js con SRI.** Se carga desde jsDelivr con versión fija y `integrity="sha384-…"` + `crossorigin`. Si el contenido del CDN cambia, el navegador lo bloquea. La CSP ya limita los scripts a `self` y jsDelivr.
- **Importación sin confiar en el formulario.**
  - La vista previa solo devuelve las decisiones del usuario por número de línea (incluir, descripción, categoría).
  - Al confirmar, el servidor **vuelve a interpretar el texto**: fechas, montos, monedas y cuotas nunca salen de campos del formulario, que alguien podría alterar.
  - Las FK (tarjeta, categoría) se validan contra los datos del propio usuario.
  - Todo se guarda en **una transacción**: si una línea falla, no se guarda ninguna.
- **Límites de tamaño.**
  - Texto pegado: hasta 30.000 caracteres y 400 líneas.
  - El formulario de confirmación sube el límite de campos solo en esa acción (`RequestFormLimits(ValueCountLimit = 4096)`).
  - Los reportes y exportaciones se acotan a 36 meses.
- **CSV sin inyección de fórmulas.** Un texto que empieza con `= + - @` se prefija con `'` para que Excel no lo ejecute (por ejemplo, una descripción `=HYPERLINK(...)`). Los campos con `;`, comillas o saltos de línea van entre comillas.
- Tests:
  - `Etapa4HttpTests`: rutas privadas, aislamiento de presupuestos y tipo de cambio, importación que ignora montos inyectados en el formulario, CSV solo con datos propios, Chart.js con SRI y sin scripts inline.
  - `Etapa4Tests`: transacción "todo o nada", categorías ajenas, historial por usuario.

## Revisión previa al despliegue (octubre 2026)

Se revisaron todos los controladores, rutas públicas, salidas sin codificar (`HtmlString`), consultas, formularios y configuración. Lo encontrado y lo que se hizo:

| # | Hallazgo | Riesgo | Estado |
|---|---|---|---|
| 1 | **ReDoS en el parser del estado de cuenta.** Una línea de 4.000 caracteres con muchos espacios hacía que la expresión regular tardara más de 70 s, y una de 16.000, horas. Cualquier usuario con sesión podía trabar el servidor; en Vercel, además, se paga la CPU. | Alto | ✅ Corregido: largo máximo por línea (300), espacios colapsados antes de evaluar y `matchTimeout` de 100 ms. Medido después: 20.005 líneas hostiles y aleatorias en 157 ms. Tests en `SeguridadExtraTests`. |
| 2 | **Registro abierto a cualquiera.** Publicada en internet, cualquiera podía crear una cuenta y usar la app (y llenar la base). | Medio | ✅ `App:CorreosPermitidos`: solo esos correos pueden registrarse. Test `Solo_los_correos_de_la_lista_pueden_registrarse`. |
| 3 | PBKDF2 con 100.000 iteraciones (el valor por defecto), por debajo de lo que recomienda OWASP para SHA-512. | Bajo | ✅ 210.000 iteraciones. |
| 4 | Al cambiar la contraseña, las otras sesiones seguían abiertas hasta 30 minutos (el intervalo por defecto del security stamp). | Bajo | ✅ 5 minutos. |
| 5 | Faltaba `Cross-Origin-Opener-Policy`. | Bajo | ✅ Agregado. |
| 6 | Límite de intentos por IP detrás de un proxy: si la IP se pudiera falsificar con `X-Forwarded-For`, el límite se podría esquivar. | Bajo | ✅ Verificado: se toma solo el último valor, el que agrega el proxy, y Vercel reemplaza el encabezado con la IP real. El bloqueo por cuenta (en la base) no depende de la IP. |
| 7 | `AllowedHosts: *`. | Bajo | 📋 Configurar `AllowedHosts` con el dominio en producción (en la guía de despliegue). |
| 8 | Enumeración de cuentas: el registro dice "ya existe una cuenta con ese correo" y el login es algo más rápido con un correo inexistente. | Bajo | Aceptado: con la lista de correos permitidos, un extraño no puede registrarse ni sacarle provecho. |
| 9 | El límite de intentos por IP vive en memoria, por instancia. Si Vercel levanta varias, cada una cuenta por separado. | Bajo | Aceptado: el bloqueo de 5 intentos por cuenta se guarda en la base y vale para todas las instancias. |
| 10 | Sin recuperación de contraseña (no hay email). | Operativo | ✅ Comando `admin reset-password` ([10-administracion.md](10-administracion.md)). |
| 11 | Sin respaldos propios de la base. | Operativo | ✅ Respaldo y restauración por usuario, más el historial de Neon. |

Revisado sin hallazgos:
- **IDOR**: filtros globales y control en `SaveChanges`; las FK del formulario se validan.
- **XSS**: toda la salida codificada; los `HtmlString` solo arman HTML fijo, con colores validados e íconos codificados.
- **CSRF**: antiforgery global.
- **SQL injection**: solo EF Core, sin SQL armado a mano.
- **Open redirect**: `ReturnUrl` solo local.
- **Mass assignment**: se usan view models; los ids salen de la ruta.
- **Secretos**: ninguno en el repo, salvo la contraseña de la cuenta de prueba local, que no existe en producción.
- **Dependencias**: sin vulnerabilidades conocidas.
- **CSV**: sin inyección de fórmulas.

## Etapa 5: respaldo, administración, cotizaciones y app instalable

- **Respaldo**:
  - La descarga es una ruta privada y solo trae los datos del usuario (pasa por los filtros globales). No incluye el hash de la contraseña.
  - La restauración es destructiva, así que pide **contraseña** (con bloqueo por intentos), **confirmación explícita** y límite de intentos por IP.
  - Acepta como máximo 4 MB y valida el archivo completo antes de tocar nada: rechaza campos desconocidos, referencias rotas, montos, colores e íconos inválidos.
  - Reemplaza los datos en **una transacción**. El borrado previo usa `ExecuteDelete` sobre las consultas filtradas, así que solo puede alcanzar filas del usuario actual. Sin usuario, no hace nada.
- **Comandos de administración**: solo corren desde una consola con acceso a la cadena de conexión, nunca por HTTP. La contraseña temporal sale del generador criptográfico y respeta la política; el reset renueva el security stamp (cierra sesiones) y quita el bloqueo.
- **Cotizaciones**: la llamada sale del servidor a una URL fija. No hay entrada del usuario en la URL, así que no hay riesgo de SSRF. La respuesta se valida (fechas, rango de valores) y un fallo no rompe la página. Es el único dato compartido entre usuarios, y es público.
- **App instalable**: el service worker no guarda páginas ni respuestas con datos, solo estáticos. En un celular compartido o perdido no quedan gastos en la caché. Al instalarla rigen las mismas cookies, sesión y CSP.
- **Puerto**: `PORT` solo se usa fuera de desarrollo. En la PC la app escucha en `localhost`, no en toda la red.
- Tests: `Etapa5Tests` y `Etapa5HttpTests`, que cubren la restauración entre cuentas sin tocar a otros, archivos inválidos, contraseña y confirmación obligatorias, reset por consola y que el service worker no guarde páginas.

## Fuera de alcance (por decisión)

- **Verificación en dos pasos**: no hace falta para un uso personal o familiar, con registro restringido.
- **Confirmación por email**: no hay proveedor de email. Por eso `RequireConfirmedAccount` sigue en `false` y la lista de correos permitidos cumple ese rol.
