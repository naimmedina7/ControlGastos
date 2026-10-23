"""Genera los íconos PNG de la app instalable (PWA) a partir del logo, sin dependencias externas.

Uso:  python tools/generar_iconos.py
Salida: src/ControlGastos.Web/wwwroot/icons/

El logo es el mismo de _Logo.cshtml: cuadrado redondeado con degradé verde y tres barras blancas.
Se dibuja con supermuestreo (4x4 por píxel) para que los bordes queden suaves.
"""
import os
import struct
import zlib

SALIDA = os.path.join(os.path.dirname(__file__), "..", "src", "ControlGastos.Web", "wwwroot", "icons")
VERDE_CLARO = (0x34, 0xD3, 0x99)
VERDE_OSCURO = (0x04, 0x78, 0x57)

# Barras en la grilla de 32x32 del SVG original: (x, y, ancho, alto, opacidad)
BARRAS = [(8, 16, 4, 8, 0.55), (14, 12, 4, 12, 0.80), (20, 8, 4, 16, 1.00)]


def dentro_redondeado(px, py, x, y, w, h, r):
    if px < x or px > x + w or py < y or py > y + h:
        return False
    cx = min(max(px, x + r), x + w - r)
    cy = min(max(py, y + r), y + h - r)
    return (px - cx) ** 2 + (py - cy) ** 2 <= r * r


def pixel(u, v, radio_fondo, escala_contenido):
    """Color RGBA en coordenadas de la grilla 32x32 (u, v en 0..32)."""
    if not dentro_redondeado(u, v, 0, 0, 32, 32, radio_fondo):
        return (0, 0, 0, 0)
    t = (u + v) / 64  # degradé diagonal
    color = [VERDE_CLARO[i] + (VERDE_OSCURO[i] - VERDE_CLARO[i]) * t for i in range(3)]
    # El contenido se escala alrededor del centro (para el ícono "maskable", que necesita margen).
    cu = 16 + (u - 16) / escala_contenido
    cv = 16 + (v - 16) / escala_contenido
    for (x, y, w, h, a) in BARRAS:
        if dentro_redondeado(cu, cv, x, y, w, h, 2):
            color = [c * (1 - a) + 255 * a for c in color]
            break
    return (*color, 255)


def png(tamano, radio_fondo, escala_contenido):
    ss = 4
    filas = bytearray()
    for py in range(tamano):
        filas.append(0)  # filtro "None"
        for px in range(tamano):
            acc = [0.0, 0.0, 0.0, 0.0]
            for sy in range(ss):
                for sx in range(ss):
                    u = (px + (sx + 0.5) / ss) * 32 / tamano
                    v = (py + (sy + 0.5) / ss) * 32 / tamano
                    r, g, b, a = pixel(u, v, radio_fondo, escala_contenido)
                    acc[0] += r * a; acc[1] += g * a; acc[2] += b * a; acc[3] += a
            alfa = acc[3] / (ss * ss)
            if acc[3] > 0:
                filas += bytes(int(round(acc[i] / acc[3])) for i in range(3)) + bytes([int(round(alfa))])
            else:
                filas += b"\x00\x00\x00\x00"

    def chunk(tipo, datos):
        return struct.pack(">I", len(datos)) + tipo + datos + struct.pack(">I", zlib.crc32(tipo + datos) & 0xFFFFFFFF)

    cabecera = struct.pack(">IIBBBBB", tamano, tamano, 8, 6, 0, 0, 0)  # RGBA de 8 bits
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", cabecera) + chunk(b"IDAT", zlib.compress(bytes(filas), 9)) + chunk(b"IEND", b"")


def main():
    os.makedirs(SALIDA, exist_ok=True)
    iconos = {
        "icon-192.png": (192, 9, 1.0),        # esquinas redondeadas, como el favicon
        "icon-512.png": (512, 9, 1.0),
        "maskable-512.png": (512, 0, 0.72),   # cuadrado completo; el sistema lo recorta (zona segura del 80 %)
        "apple-touch-icon.png": (180, 0, 0.85),  # iOS redondea las esquinas por su cuenta
    }
    for nombre, (tamano, radio, escala) in iconos.items():
        with open(os.path.join(SALIDA, nombre), "wb") as f:
            f.write(png(tamano, radio, escala))
        print("ok", nombre)


if __name__ == "__main__":
    main()
