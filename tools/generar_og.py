"""Genera la imagen para compartir el link (Open Graph, 1200 × 630) con la paleta y la tipografía de la marca.

Uso:  python tools/generar_og.py      (requiere: pip install pillow fonttools brotli)
Salida: src/ControlGastos.Web/wwwroot/icons/og-imagen.png

Los importes de la tarjeta son inventados: es una imagen pública.
"""
import io
import os

from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont
from PIL import Image, ImageDraw, ImageFont

BASE = os.path.join(os.path.dirname(__file__), "..", "src", "ControlGastos.Web", "wwwroot")
FUENTE = os.path.join(BASE, "fonts", "bricolage-grotesque-latin.woff2")
SALIDA = os.path.join(BASE, "icons", "og-imagen.png")

TINTA, TINTA_2 = (0x0B, 0x1F, 0x1A), (0x14, 0x33, 0x2B)
MENTA, AMBAR, CRUDO, GRIS = (0x6E, 0xE7, 0xB7), (0xFB, 0xBF, 0x24), (0xF6, 0xF4, 0xEF), (0xB9, 0xC7, 0xC0)


def fuente(peso, tamano):
    """Instancia de la fuente variable en un peso dado, como TTF en memoria (Pillow no lee woff2 variable)."""
    f = instantiateVariableFont(TTFont(FUENTE), {"wght": peso, "opsz": 96})
    f.flavor = None
    buf = io.BytesIO()
    f.save(buf)
    buf.seek(0)
    return ImageFont.truetype(buf, tamano)


def main():
    img = Image.new("RGB", (1200, 630), TINTA)
    d = ImageDraw.Draw(img)

    # Logo: tres barras ascendentes (como el ícono de la app).
    x0, y0 = 80, 78
    d.rounded_rectangle([x0, y0, x0 + 64, y0 + 64], radius=18, fill=(0x04, 0x78, 0x57))
    for (bx, by, alto, op) in [(16, 32, 16, .55), (28, 24, 24, .8), (40, 16, 32, 1)]:
        c = tuple(int(0x04 + (255 - 0x04) * op) if i == 0 else int([0x78, 0x57][i - 1] + (255 - [0x78, 0x57][i - 1]) * op) for i in range(3))
        d.rounded_rectangle([x0 + bx, y0 + by, x0 + bx + 8, y0 + by + alto], radius=4, fill=c)
    d.text((x0 + 84, y0 + 8), "Mis·Gastos", font=fuente(700, 44), fill=CRUDO)

    # Tarjeta de ejemplo (a la derecha): escalera de cuotas que bajan mes a mes (montos inventados).
    cx, cy, cw, ch = 880, 150, 240, 330
    limite = cx - 40 - 80  # ancho máximo del texto, para no tocar la tarjeta

    # Titular: el tamaño más grande que entra en el ancho disponible.
    tam = 76
    while tam > 40 and d.textlength("Tus tarjetas y tus cuotas,", font=fuente(800, tam)) > limite:
        tam -= 2
    t1 = fuente(800, tam)
    d.text((80, 210), "Tus tarjetas y tus cuotas,", font=t1, fill=CRUDO)
    d.text((80, 210 + int(tam * 1.12)), "claras mes a mes.", font=t1, fill=MENTA)
    sub = fuente(500, 28)
    d.text((80, 410), "Gastos en pesos y dólares, cuotas que", font=sub, fill=GRIS)
    d.text((80, 450), "se reparten solas y reportes simples.", font=sub, fill=GRIS)

    d.rounded_rectangle([cx, cy, cx + cw, cy + ch], radius=28, fill=TINTA_2)
    d.text((cx + 26, cy + 24), "Cuotas · próximos meses", font=fuente(600, 17), fill=GRIS)
    d.text((cx + 26, cy + 54), "$ 18.320", font=fuente(800, 42), fill=CRUDO)
    alturas = [190, 160, 128, 96, 64, 36]
    ancho, sep, base = 24, 10, cy + ch - 34
    for i, a in enumerate(alturas):
        bx = cx + 26 + i * (ancho + sep)
        color = AMBAR if i == 0 else (0x3B, 0x5C, 0x52)
        d.rounded_rectangle([bx, base - a, bx + ancho, base], radius=13, fill=color)

    d.text((80, 560), "Hecho por Fourvexa", font=fuente(600, 22), fill=(0x8F, 0xA5, 0x9A))
    img.save(SALIDA, optimize=True)
    print("ok", SALIDA, os.path.getsize(SALIDA) // 1024, "KB")


if __name__ == "__main__":
    main()
