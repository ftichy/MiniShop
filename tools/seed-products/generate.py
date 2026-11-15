#!/usr/bin/env python3
"""Vygeneruje src/MiniShop.Inventory.Api/SeedData (products.json + images/*.png) – issue #115.

Čistý Python bez knihoven. Obrázky jsou ilustrace (barevný přechod + siluetka produktu), ne fotky;
skutečnou fotku lze později vyměnit v administraci produktu. Spuštění: python3 tools/seed-products/generate.py
"""
import json, math, os, struct, zlib

OUT = os.path.join(os.path.dirname(__file__), "..", "..", "src", "MiniShop.Inventory.Api", "SeedData")
SIZE = 480

# (soubor, název, popis, cena Kč s DPH, sklad, kategorie, tvar, barva)
PRODUCTS = [
    ("nordix-air-14", "Notebook Nordix Air 14", "14\" IPS 1920×1200, Intel Core Ultra 5, 16 GB RAM, SSD 512 GB, Wi-Fi 6E, podsvícená klávesnice, hmotnost 1,3 kg. Tenký a lehký notebook na cesty i do školy, baterie vydrží až 14 hodin.", 18990, 14, "Notebooky", "laptop", (79, 70, 229)),
    ("nordix-pro-16", "Notebook Nordix Pro 16", "16\" QHD+ 165 Hz, AMD Ryzen 7, 32 GB RAM, SSD 1 TB, grafika RTX 4060 8 GB. Výkon pro programování, střih videa i 3D, kvalitní chlazení a hliníkové tělo.", 34990, 6, "Notebooky", "laptop", (14, 116, 144)),
    ("voltra-strike-15", "Herní notebook Voltra Strike 15", "15,6\" FHD 240 Hz, Intel Core i7, 16 GB DDR5, SSD 1 TB, grafika RTX 4070 8 GB, RGB klávesnice. Herní výkon s pokročilým chlazením a rychlým displejem.", 39990, 3, "Notebooky", "laptop", (190, 24, 93)),
    ("nordix-go-15", "Notebook Nordix Go 15", "15,6\" FHD, Intel Core i3, 8 GB RAM, SSD 256 GB, numerická klávesnice. Spolehlivý kancelářský notebook za rozumnou cenu.", 12490, 25, "Notebooky", "laptop", (71, 85, 105)),
    ("nordix-x5", "Smartphone Nordix X5 128 GB", "6,5\" AMOLED 120 Hz, 8 GB RAM, úložiště 128 GB, trojitý fotoaparát 50 Mpx, baterie 5000 mAh, rychlé nabíjení 45 W, podpora 5G a dual SIM.", 7490, 40, "Telefony a tablety", "phone", (225, 29, 72)),
    ("nordix-x7-pro", "Smartphone Nordix X7 Pro 256 GB", "6,7\" LTPO AMOLED 144 Hz, 12 GB RAM, 256 GB, fotoaparát 200 Mpx s optickou stabilizací, bezdrátové nabíjení 50 W, hliníkový rám, odolnost IP68.", 15990, 12, "Telefony a tablety", "phone", (124, 58, 237)),
    ("nordix-tab-11", "Tablet Nordix Tab 11", "11\" displej 2K 120 Hz, 8 GB RAM, 256 GB, čtyři reproduktory, baterie 8000 mAh. Vhodný ke čtení, filmům i poznámkám s perem.", 8990, 9, "Telefony a tablety", "tablet", (13, 148, 136)),
    ("nordix-tab-lite-8", "Tablet Nordix Tab Lite 8", "8\" HD displej, 4 GB RAM, 64 GB, Wi-Fi, lehký a skladný. Ideální pro děti, kuchyň nebo jako čtečka.", 3490, 0, "Telefony a tablety", "tablet", (217, 119, 6)),
    ("voltra-24-fhd", "Monitor Voltra 24\" Full HD", "24\" IPS 1920×1080, 75 Hz, 5 ms, HDMI + DisplayPort, tenké rámečky, naklápění. Základní monitor do kanceláře.", 3290, 30, "Monitory", "monitor", (37, 99, 235)),
    ("voltra-27-4k", "Monitor Voltra 27\" 4K UHD", "27\" IPS 3840×2160, 60 Hz, pokrytí sRGB 99 %, USB-C s napájením 65 W, výškově nastavitelný stojan. Ostrý obraz pro práci i grafiku.", 8990, 8, "Monitory", "monitor", (8, 145, 178)),
    ("voltra-34-uw", "Ultraširoký monitor Voltra 34\" UWQHD", "34\" zakřivený VA 3440×1440, 144 Hz, poměr 21:9, HDR400, AMD FreeSync. Víc místa pro okna i do her.", 11990, 4, "Monitory", "ultrawide", (147, 51, 234)),
    ("voltra-27-240", "Herní monitor Voltra 27\" 240 Hz", "27\" QHD IPS 2560×1440, 240 Hz, odezva 1 ms, G-Sync Compatible, DisplayPort 1.4. Plynulý obraz pro soutěžní hry.", 9490, 2, "Monitory", "monitor", (234, 88, 12)),
    ("ssd-nvme-1tb", "SSD disk NVMe 1 TB", "M.2 2280 PCIe 4.0 x4, čtení až 7000 MB/s, zápis až 5300 MB/s, životnost 600 TBW. Rychlé úložiště pro PC i herní konzole.", 2190, 60, "Počítačové komponenty", "ssd", (22, 163, 74)),
    ("ssd-nvme-2tb", "SSD disk NVMe 2 TB", "M.2 2280 PCIe 4.0 x4, čtení až 7300 MB/s, zápis až 6600 MB/s, chladič v balení, životnost 1200 TBW.", 3990, 35, "Počítačové komponenty", "ssd", (5, 150, 105)),
    ("ram-ddr5-32", "Paměť DDR5 32 GB (2× 16 GB) 6000 MHz", "Sada dvou modulů DDR5-6000, CL30, profil XMP/EXPO, hliníkové chladiče. Pro moderní desky AM5 a LGA1700.", 2990, 28, "Počítačové komponenty", "ram", (219, 39, 119)),
    ("gpu-rtx-4060", "Grafická karta GeForce RTX 4060 8 GB", "8 GB GDDR6, PCIe 4.0, DLSS 3, ray tracing, tři DisplayPorty a HDMI 2.1, dvouventilátorové chlazení. Plynulé hraní ve Full HD i QHD.", 8990, 2, "Počítačové komponenty", "gpu", (21, 128, 61)),
    ("cpu-8core-am5", "Procesor 8jádrový AM5 4,7 GHz", "8 jader / 16 vláken, boost až 5,3 GHz, 32 MB cache, patice AM5, integrovaná grafika, bez chladiče v balení.", 6490, 11, "Počítačové komponenty", "cpu", (185, 28, 28)),
    ("psu-750-gold", "PC zdroj 750 W 80 PLUS Gold", "Plně modulární zdroj, účinnost 80 PLUS Gold, tichý ventilátor 135 mm, konektor 12VHPWR pro moderní grafické karty, záruka 7 let.", 2390, 17, "Počítačové komponenty", "psu", (82, 82, 91)),
    ("voltra-mys-pro", "Herní myš Voltra Pro Wireless", "Bezdrátová myš 2,4 GHz, senzor 26 000 DPI, váha 63 g, 6 programovatelných tlačítek, výdrž baterie 70 hodin.", 1190, 45, "Periferie", "mouse", (99, 102, 241)),
    ("nordix-klavesnice-bt", "Bezdrátová klávesnice Nordix Slim", "Nízkoprofilová klávesnice s Bluetooth pro 3 zařízení, CZ layout, tiché klávesy, USB-C nabíjení, výdrž až 4 měsíce.", 1490, 22, "Periferie", "keyboard", (100, 116, 139)),
    ("nordix-webkamera-fhd", "Webkamera Nordix Cam Full HD", "1080p / 30 fps, automatické ostření, dva mikrofony s potlačením šumu, krytka objektivu, připojení USB-A.", 790, 33, "Periferie", "webcam", (2, 132, 199)),
    ("nordix-sluchatka-anc", "Bluetooth sluchátka Nordix Quiet ANC", "Bezdrátová sluchátka s aktivním potlačením hluku, Bluetooth 5.3, výdrž 40 hodin, multipoint, skládací konstrukce s pouzdrem.", 2990, 18, "Audio", "headphones", (126, 34, 206)),
    ("nordix-reproduktor-bt", "Bluetooth reproduktor Nordix Boom", "Přenosný reproduktor 20 W, voděodolný IP67, výdrž 16 hodin, možnost spárovat dva kusy do stereo dvojice.", 1290, 26, "Audio", "speaker", (234, 88, 12)),
    ("nordix-router-ax3000", "Wi-Fi 6 router Nordix AX3000", "Dvoupásmový router Wi-Fi 6 až 3000 Mb/s, 4× gigabitový LAN, WPA3, rodičovská kontrola, snadné nastavení v aplikaci.", 2190, 20, "Síťové prvky", "router", (30, 64, 175)),
    ("nordix-mesh-3", "Wi-Fi mesh systém Nordix Home (3 jednotky)", "Tří-jednotkový mesh Wi-Fi 6 AX1800 pro pokrytí domu do 400 m², jedno jméno sítě, automatické přepínání zařízení.", 5990, 7, "Síťové prvky", "mesh", (15, 118, 110)),
    ("nordix-switch-8", "Switch Nordix 8× Gigabit", "Nespravovaný 8portový gigabitový switch, kovový kryt, tichý provoz bez ventilátoru, plug and play.", 590, 50, "Síťové prvky", "switch", (63, 63, 70)),
    ("nordix-powerbanka-20k", "Powerbanka Nordix 20 000 mAh", "Kapacita 20 000 mAh, USB-C PD 45 W + 2× USB-A, displej s procenty, dobije notebook i telefon.", 890, 38, "Příslušenství", "powerbank", (217, 119, 6)),
    ("nordix-nabijecka-gan-65", "Nabíječka GaN 65 W", "Kompaktní GaN nabíječka 65 W se dvěma USB-C a jedním USB-A, nabije notebook, tablet i telefon najednou.", 790, 42, "Příslušenství", "charger", (79, 70, 229)),
    ("nordix-hub-7v1", "USB-C hub 7v1", "Rozbočovač USB-C: HDMI 4K/60, 2× USB-A 3.0, čtečka SD a microSD, gigabitový LAN, průchozí napájení 100 W.", 1290, 24, "Příslušenství", "hub", (71, 85, 105)),
    ("hdmi-21-2m", "Kabel HDMI 2.1 8K, 2 m", "Certifikovaný kabel HDMI 2.1 s podporou 8K/60 a 4K/120, eARC, pozlacené konektory, opletený.", 299, 80, "Kabely", "cable", (190, 24, 93)),
]


def lerp(a, b, t):
    return a + (b - a) * t


def sd_rrect(px, py, cx, cy, hw, hh, r):
    qx, qy = abs(px - cx) - hw + r, abs(py - cy) - hh + r
    return math.hypot(max(qx, 0), max(qy, 0)) + min(max(qx, qy), 0) - r


def sd_circle(px, py, cx, cy, r):
    return math.hypot(px - cx, py - cy) - r


def rr(cx, cy, w, h, r, color, alpha=1.0):
    return ("r", cx, cy, w / 2, h / 2, min(r, w / 2, h / 2), color, alpha)


def ci(cx, cy, r, color, alpha=1.0):
    return ("c", cx, cy, r, color, alpha)


WHITE, DARK, GLASS = (255, 255, 255), (30, 41, 59), (226, 232, 240)


def shapes(kind, c):
    dark = tuple(int(v * 0.35) for v in c)
    light = tuple(int(lerp(v, 255, 0.55)) for v in c)
    cx, cy = SIZE / 2, SIZE / 2
    if kind == "laptop":
        return [rr(cx, 200, 300, 190, 14, DARK), rr(cx, 200, 276, 166, 6, c), rr(cx, 200, 276, 166, 6, light, 0.25),
                rr(cx, 320, 380, 26, 12, GLASS), rr(cx, 316, 90, 8, 4, (148, 163, 184))]
    if kind == "phone":
        return [rr(cx, cy, 170, 330, 30, DARK), rr(cx, cy, 150, 306, 20, c), rr(cx, 128, 56, 10, 5, DARK),
                ci(cx, 380, 9, light), rr(cx, 220, 110, 8, 4, light, 0.5), rr(cx, 250, 80, 8, 4, light, 0.4)]
    if kind == "tablet":
        return [rr(cx, cy, 320, 240, 22, DARK), rr(cx, cy, 296, 216, 10, c),
                rr(cx, cy, 120, 10, 5, light, 0.5), rr(cx - 40, cy + 30, 120, 8, 4, light, 0.35)]
    if kind in ("monitor", "ultrawide"):
        w, h = (380, 150) if kind == "ultrawide" else (330, 200)
        top = 200 if kind == "monitor" else 220
        return [rr(cx, top, w, h, 12, DARK), rr(cx, top, w - 20, h - 20, 5, c), rr(cx, top, w - 20, h - 20, 5, light, 0.2),
                rr(cx, top + h / 2 + 24, 40, 50, 4, (100, 116, 139)), rr(cx, top + h / 2 + 52, 150, 14, 7, (100, 116, 139))]
    if kind == "ssd":
        return [rr(cx, cy, 340, 110, 10, DARK), rr(cx - 20, cy, 280, 90, 6, c), rr(cx + 140, cy, 40, 110, 4, (234, 179, 8)),
                rr(cx - 100, cy, 70, 50, 4, dark), rr(cx + 10, cy, 70, 50, 4, dark), rr(cx + 110 - 20, cy, 24, 50, 4, light, 0.7)]
    if kind == "ram":
        out = [rr(cx, cy, 370, 90, 8, c), rr(cx, cy + 56, 330, 20, 2, (234, 179, 8))]
        out += [rr(cx - 120 + i * 80, cy - 6, 60, 50, 4, dark) for i in range(4)]
        return out
    if kind == "gpu":
        return [rr(cx, cy, 380, 170, 16, DARK), rr(cx, cy, 360, 150, 10, c), ci(cx - 90, cy, 56, dark), ci(cx + 90, cy, 56, dark),
                ci(cx - 90, cy, 18, light), ci(cx + 90, cy, 18, light), rr(cx, cy + 100, 300, 16, 3, (234, 179, 8))]
    if kind == "cpu":
        out = [rr(cx, cy, 220, 220, 18, c), rr(cx, cy, 130, 130, 10, dark), rr(cx, cy, 90, 90, 6, light, 0.5)]
        for i in range(6):
            o = -85 + i * 34
            out += [rr(cx + o, cy - 130, 10, 28, 3, (203, 213, 225)), rr(cx + o, cy + 130, 10, 28, 3, (203, 213, 225)),
                    rr(cx - 130, cy + o, 28, 10, 3, (203, 213, 225)), rr(cx + 130, cy + o, 28, 10, 3, (203, 213, 225))]
        return out
    if kind == "psu":
        return [rr(cx, cy, 300, 200, 16, DARK), rr(cx, cy, 280, 180, 10, c), ci(cx - 30, cy, 70, dark), ci(cx - 30, cy, 20, light),
                rr(cx + 110, cy - 50, 30, 14, 4, light, 0.6), rr(cx + 110, cy, 30, 14, 4, light, 0.6), rr(cx + 110, cy + 50, 30, 14, 4, light, 0.6)]
    if kind == "mouse":
        return [rr(cx, cy, 170, 270, 80, DARK), rr(cx, cy, 150, 250, 70, c), rr(cx, 170, 6, 100, 3, DARK), rr(cx, 200, 22, 56, 10, light)]
    if kind == "keyboard":
        out = [rr(cx, cy, 400, 170, 18, DARK), rr(cx, cy, 384, 154, 12, c, 0.9)]
        for row in range(3):
            for col in range(10):
                out.append(rr(cx - 162 + col * 36, cy - 48 + row * 36, 28, 28, 6, light, 0.85))
        out.append(rr(cx, cy + 60, 200, 22, 6, light, 0.85))
        return out
    if kind == "webcam":
        return [rr(cx, 190, 210, 120, 40, DARK), ci(cx, 190, 44, c), ci(cx, 190, 24, DARK), ci(cx - 14, 176, 8, WHITE, 0.8),
                rr(cx, 285, 50, 46, 6, (100, 116, 139)), rr(cx, 318, 140, 16, 8, (100, 116, 139))]
    if kind == "headphones":
        return [("o", cx, 250, 120, 15, DARK, 1.0, 290),
                rr(cx - 125, 290, 74, 140, 30, c), rr(cx + 125, 290, 74, 140, 30, c), rr(cx - 125, 290, 40, 100, 18, dark),
                rr(cx + 125, 290, 40, 100, 18, dark)]
    if kind == "speaker":
        return [rr(cx, cy, 300, 180, 80, DARK), rr(cx, cy, 284, 164, 70, c), ci(cx - 70, cy, 50, dark), ci(cx + 70, cy, 50, dark),
                ci(cx - 70, cy, 16, light), ci(cx + 70, cy, 16, light)]
    if kind in ("router", "mesh"):
        out = [rr(cx, 300, 330, 60, 18, DARK), rr(cx, 300, 310, 40, 12, c), ci(cx - 110, 300, 5, light), ci(cx - 90, 300, 5, light)]
        for dx in (-120, 0, 120):
            out.append(rr(cx + dx, 195, 20, 150, 8, DARK))
        return out
    if kind == "switch":
        out = [rr(cx, cy, 380, 100, 12, DARK), rr(cx, cy, 364, 84, 8, c)]
        out += [rr(cx - 140 + i * 40, cy, 28, 32, 4, dark) for i in range(8)]
        out += [ci(cx - 140 + i * 40, cy - 28, 4, light) for i in range(8)]
        return out
    if kind == "powerbank":
        return [rr(cx, cy, 170, 300, 26, DARK), rr(cx, cy, 150, 280, 18, c), rr(cx, 170, 90, 12, 6, light, 0.8),
                rr(cx, 205, 90, 12, 6, light, 0.6), rr(cx, 240, 90, 12, 6, light, 0.4), rr(cx, 340, 50, 12, 4, DARK)]
    if kind == "charger":
        return [rr(cx, 230, 190, 190, 30, DARK), rr(cx, 230, 170, 170, 22, c), rr(cx - 40, 330, 18, 60, 4, (203, 213, 225)),
                rr(cx + 40, 330, 18, 60, 4, (203, 213, 225)), rr(cx - 36, 210, 54, 18, 9, dark), rr(cx + 36, 210, 54, 18, 9, dark),
                rr(cx, 252, 54, 18, 9, dark)]
    if kind == "hub":
        out = [rr(cx, cy, 330, 120, 22, DARK), rr(cx, cy, 314, 104, 16, c), rr(cx - 230, cy, 110, 14, 7, DARK)]
        out += [rr(cx - 110 + i * 56, cy, 40, 22, 4, dark) for i in range(5)]
        return out
    if kind == "cable":
        out = []
        for t in range(0, 101, 2):
            x = lerp(60, 420, t / 100)
            y = 240 + 80 * math.sin(t / 100 * math.pi * 2)
            out.append(ci(x, y, 9, c))
        out += [rr(60, 240, 50, 36, 8, DARK), rr(420, 240, 50, 36, 8, DARK)]
        return out
    raise ValueError(kind)


def render(kind, color):
    top = tuple(int(lerp(v, 255, 0.88)) for v in color)
    bottom = tuple(int(lerp(v, 255, 0.70)) for v in color)
    sh = shapes(kind, color)
    # obvod každého tvaru pro rychlé přeskočení
    boxes = []
    for s in sh:
        if s[0] == "r":
            _, cx, cy, hw, hh, r, col, al = s
            boxes.append((cx - hw - 3, cx + hw + 3, cy - hh - 3, cy + hh + 3))
        elif s[0] == "o":
            _, cx, cy, r, th, col, al, ymax = s
            boxes.append((cx - r - th - 3, cx + r + th + 3, cy - r - th - 3, ymax))
        else:
            _, cx, cy, r, col, al = s
            boxes.append((cx - r - 3, cx + r + 3, cy - r - 3, cy + r + 3))
    # měkký stín pod produktem
    rows = bytearray()
    for y in range(SIZE):
        t = y / SIZE
        base = [lerp(top[i], bottom[i], t) for i in range(3)]
        rows.append(0)
        for x in range(SIZE):
            px, py = x + 0.5, y + 0.5
            col = base
            for s, (x0, x1, y0, y1) in zip(sh, boxes):
                if px < x0 or px > x1 or py < y0 or py > y1:
                    continue
                if s[0] == "r":
                    d = sd_rrect(px, py, s[1], s[2], s[3], s[4], s[5])
                    color_s, alpha = s[6], s[7]
                elif s[0] == "o":
                    d = abs(sd_circle(px, py, s[1], s[2], s[3])) - s[4]
                    color_s, alpha = s[5], s[6]
                else:
                    d = sd_circle(px, py, s[1], s[2], s[3])
                    color_s, alpha = s[4], s[5]
                cov = min(1.0, max(0.0, 0.5 - d)) * alpha
                if cov > 0:
                    col = [col[i] * (1 - cov) + color_s[i] * cov for i in range(3)]
            rows += bytes(int(v + 0.5) for v in col)
    return rows


def png(rows):
    def chunk(tag, data):
        c = struct.pack(">I", len(data)) + tag + data
        return c + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", SIZE, SIZE, 8, 2, 0, 0, 0)) +
            chunk(b"IDAT", zlib.compress(bytes(rows), 9)) + chunk(b"IEND", b""))


def main():
    os.makedirs(os.path.join(OUT, "images"), exist_ok=True)
    items = []
    for file, name, desc, price, stock, cat, kind, color in PRODUCTS:
        assert len(name) <= 100 and len(desc) <= 500, name
        with open(os.path.join(OUT, "images", file + ".png"), "wb") as f:
            f.write(png(render(kind, color)))
        items.append({"name": name, "description": desc, "price": price, "stockQuantity": stock,
                      "category": cat, "image": file + ".png"})
    with open(os.path.join(OUT, "products.json"), "w", encoding="utf-8") as f:
        json.dump({"products": items}, f, ensure_ascii=False, indent=2)
        f.write("\n")
    print(len(items), "produktů")


if __name__ == "__main__":
    main()
