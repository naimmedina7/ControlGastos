# 11 · Diseño

El sistema visual de Mis·Gastos: colores, tipografía, formas, componentes y las reglas de accesibilidad con las que se verificó. Todo vive en [site.css](../src/ControlGastos.Web/wwwroot/css/site.css). Los valores salen de los tokens de `:root` y no hay colores sueltos fuera de ese bloque, salvo los de las categorías y tarjetas, que elige el usuario.

## Principios

- **La cifra manda.** Lo que importa en cada pantalla es un número (cuánto gastaste, cuánto te queda), y se ve grande, con la fuente de títulos y dígitos tabulares.
- **El color tiene un trabajo.** El verde es la acción y el "entra plata"; el ámbar son las cuotas; el rojo es solo para errores y excesos. Nada se identifica únicamente por color: siempre hay un texto al lado.
- **Plano y con bordes.** Las tarjetas son blancas sobre un fondo crudo, con borde de 1 px y sin degradados ni brillos.
- **Poco movimiento.** Solo crecen las barras al cargar y entra la tarjeta de ejemplo del login. Con "reducir movimiento" no se anima nada.

## Colores

| Token | Valor | Uso | Contraste |
|---|---|---|---|
| `--bg` | `#f6f4ef` | Fondo (crudo cálido) | — |
| `--surface` | `#ffffff` | Tarjetas | sobre crudo 1,1:1, por eso **toda tarjeta lleva borde** |
| `--border` / `--border-strong` | `#e1e6e1` / `#cad3cd` | Bordes de tarjetas / de campos | — |
| `--text` | `#14221c` | Texto principal | 15:1 sobre crudo |
| `--text-2` | `#5b6b63` | Texto secundario | 5,1:1 crudo · 5,6:1 blanco |
| `--muted` | `#647069` | Rótulos y ayudas | 4,7:1 crudo · 5,2:1 blanco |
| `--accent` | `#047857` | Botones, enlaces, "te queda" | blanco encima 5,5:1 |
| `--accent-soft` | `#e3f2ea` | Fondos de estado ok | acento encima 4,7:1 |
| `--ink` | `#0b1f1a` | Lateral, tarjeta principal, pantalla de acceso | — |
| `--ink-text` / `--ink-label` | `#b9c7c0` / `#8fa59a` | Texto sobre tinta | 9,8:1 / 6,5:1 |
| `--mint` | `#6ee7b7` | Acento sobre tinta | 11,2:1 |
| `--amber` | `#fbbf24` | Cuotas sobre tinta, barras | 10,3:1 sobre tinta |
| `--amber-ink` | `#b45309` | Cuotas sobre fondo claro | 4,6:1 crudo · 5,0:1 blanco |
| `--amber-deep` / `--amber-soft` | `#92400e` / `#fdf3dc` | Pastilla de cuota (`3/12`) | 6,4:1 |
| `--danger` / `--danger-ink` | `#dc2626` / `#b91c1c` | Errores, excesos | 4,8:1 / 6,5:1 sobre blanco |

### Paleta de los gráficos (no tocar)

`--series-1` `#2a78d6`, `--series-2` `#eb6834`, `--series-3` `#1baf7a`: contado, cuotas y fijos en Reportes. Está validada para daltonismo (separación entre pares y contraste). El ámbar de las cuotas **no** la reemplaza; cambiarla obliga a validarla de nuevo.

### Colores de categorías y tarjetas

- **Categorías**: el ícono va sobre un fondo sólido de su color, con el ícono en blanco. Si el color es claro (puede venir de un respaldo), `Fmt.ColorIconoSobre` pasa el ícono a verde tinta para no bajar de 3:1. "Sin categoría" es `#64748b`.
- **Tarjetas de pago**: la franja de color lleva un velo de tinta al 24 %, así el texto blanco supera 5:1 con toda la paleta (el naranja solo daba 3,6:1).

## Tipografía

- **Títulos y cifras**: Bricolage Grotesque (licencia OFL), alojada en el sitio (`wwwroot/fonts/`, 75 KB, solo latín) y precargada en `_Head`. Tiene una fuente de respaldo con las mismas medidas (Arial con `size-adjust` y `ascent/descent-override` calculados con fontTools), así nada salta mientras carga.
- **Texto**: Inter.
- **Cifras**: `font-variant-numeric: tabular-nums` en montos, tablas y totales. Bricolage trae dígitos proporcionales por defecto y sin esto las columnas no alinean.
- **Tamaños**: base de 15 px y **ningún texto por debajo de 12 px** (`.8rem`); `small` tiene piso de 12 px. Las cifras principales van a 40–48 px en escritorio y a 36–40 px en el celular.

## Formas

- Radio de **12 px** en controles (botones, campos, selects) y de **20 px** en tarjetas.
- Borde de 1 px en todas las tarjetas; sombra mínima (`--shadow-1`) y una más marcada solo para menús y avisos (`--shadow-pop`).
- Motivo de marca: barras con la punta redondeada, como el logo (gráfico del panel, escalera de cuotas, tarjeta de ejemplo).

## Componentes

| Componente | Dónde | Notas |
|---|---|---|
| **Bento del panel** | Inicio | La tarjeta del total en pesos ocupa 2 × 2 (con el desglose en cuotas, fijos y contado); al lado, dólares y cuotas; abajo, lo comprometido con su escalera. En tablet pasa a 2 columnas y en el celular a 1. |
| **Cuánto te queda** (`_Balance`) | Inicio e Ingresos | Ingresos del mes − gastos del mes, **por moneda**. La barra muestra qué parte de lo que entró ya se fue (verde, ámbar desde 80 %, rojo si te pasaste) y siempre lleva una frase que lo dice. Sin ingresos en una moneda solo muestra lo gastado, sin alarmas. |
| **Gráfica circular** (`_Dona`) | Gastos del mes y Reportes | Ver abajo. |
| **Avance de cuotas** (`Fmt.ProgresoCuotas`) | Gastos del mes, Cuotas pendientes | Un segmento por cuota hasta 12; con más, barra continua. Decorativo: el texto de al lado dice cuántas faltan. |
| **Estados vacíos** (`_VistaVacia`) | Listas | Una vista previa en gris de cómo se va a ver la lista, con el mismo texto de siempre. |
| **"Más detalles"** | Nuevo gasto | Fecha, categoría, cuotas, comercio y notas quedan plegados. Se abre al editar, con datos cargados o con errores; la validación lo abre antes de enfocar el campo. El aviso de cierre de la tarjeta queda siempre a la vista. |
| **Pantallas de acceso** | `/`, login, registro | `/` es el login. En escritorio, la marca a la izquierda con una tarjeta de ejemplo (importes inventados); en celular y tablet, un mensaje corto arriba del formulario. El registro marca los requisitos de la contraseña mientras escribes. |

### Gráfica circular por categoría

- **Forma**: dona, para ver de un vistazo la parte de cada categoría en el total. Como máximo **6 porciones**: las 5 categorías más grandes y el resto junto en "Otras (n)". Con más, una dona ya no se lee.
- **Color**: el de cada categoría. Como dos categorías pueden compartir color (o tener grises parecidos), la identidad nunca depende solo del color:
  - La **leyenda** lista color, nombre, porcentaje y monto. Es la versión accesible; el SVG es decorativo (`aria-hidden`).
  - Al **pasar el mouse** (o tocar, en el celular) una porción, se resalta, las demás se atenúan y un tooltip dice la categoría, el % y el monto. Pasar por una fila de la leyenda resalta su porción.
- **Trazo**: 2 px de aire entre porciones, para separar dos porciones vecinas del mismo color.
- **Monedas**: una dona por moneda; pesos y dólares no se suman. En Reportes sigue la moneda elegida (pesos, dólares o todo en pesos) y tiene su tabla de datos.
- **Implementación**: SVG generado en el servidor (cada porción es un arco con `stroke-dasharray`). No depende de Chart.js, así sirve en cualquier página. El tooltip lo maneja `site.js` (`[data-dona]`) y arma el texto con `textContent`, sin HTML.

## Movimiento

- Las barras del panel y de la escalera crecen una vez al cargar (`@keyframes crecer`), escalonadas 50 ms.
- La tarjeta de ejemplo del login entra una vez (`@keyframes entrar`).
- No hay contadores que suben, rebotes ni animaciones en bucle (salvo el indicador de carga).
- `prefers-reduced-motion: reduce` apaga todo. La barra de carga queda fija y la ruedita gira lento.

## Accesibilidad: cómo se verificó

Cada fase del rediseño se revisó con una auditoría en el navegador, a **375, 768 y 1440 px**, sobre todas las páginas:
- Sin scroll horizontal.
- Ningún texto menor a 12 px.
- Contraste de al menos 4,5:1 (3:1 en texto grande).
- Áreas táctiles de al menos 44 × 44 px en pantallas táctiles (`(max-width: 991.98px), (pointer: coarse)`).

Además hay tests automáticos (`DisenoTests`, `IngresosTests`) para lo que se puede comprobar en el HTML: tamaños de letra, tokens, contraste del ícono de categoría, estructura de la dona, "Más detalles" y las pantallas de acceso.

## Al cambiar el diseño

- Subir `VERSION` en `wwwroot/sw.js` cada vez que cambian `site.css` o `site.js`. El service worker usa primero la versión exacta del archivo, pero así se limpian las cachés viejas.
- Un color nuevo va como token en `:root`, con su contraste anotado.
- La imagen para compartir el link (`icons/og-imagen.png`, 1200 × 630) se genera con `python tools/generar_og.py`, con la misma fuente y paleta.
