#!/usr/bin/env python3
"""Генератор Assets/Config/GraphicsCatalog.json — каталога графики для симуляции.

Симуляция не читает ассеты: размер корабля, Owner/Race-подпапки корпусов, варианты атласов
астероидов и случайные текстуры планет/спутников она берёт из этого каталога
(SRG.Simulation.GraphicsCatalog). Поэтому расчёт хода не зависит от рендера и одинаков
в игре и в headless-прогоне.

  Sheets — спрайт-листы (пара foo.png + foo.png.json) такими, какими их видит игра после импорта
           в Unity: ширина — уже с учётом maxTextureSize (Unity ужимает текстуру, не влезающую
           в лимит, по большей стороне).
  Images — остальные картинки (одиночные спрайты/текстуры).

Пути — Resources-относительные без расширения. Запуск из корня репозитория после добавления,
удаления или замены картинки в Assets/Resources/Graphics:

    python3 tools/graphics-catalog/gen_graphics_catalog.py

Синхронность каталога с ассетами проверяет EditMode-тест GraphicsCatalogTests.
"""
import json
import os
import re
import struct
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
RESOURCES = os.path.join(ROOT, "Assets", "Resources")
OUTPUT = os.path.join(ROOT, "Assets", "Config", "GraphicsCatalog.json")
PIXELS_PER_UNIT = 100  # GraphicsManager режет листы с pixelsPerUnit = 100
IMAGE_EXTENSIONS = (".png", ".jpg")


def png_size(path):
    with open(path, "rb") as f:
        head = f.read(24)
    if head[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError(f"не PNG: {path}")
    return struct.unpack(">II", head[16:24])


# Assets/Editor/MyTexturePostprocessor.cs выставляет этим папкам maxTextureSize = 8192 при импорте,
# поверх значения в .meta.
POSTPROCESSOR_8192 = ("Resources/Graphics/SpaceObjects/Stars", "Resources/Graphics/SpaceObjects/Planets/Textures")


def max_texture_size(png_path, meta_text):
    """Лимит импорта: правило постпроцессора, иначе Standalone-override, если он включён,
    иначе платформа по умолчанию."""
    if any(p in png_path.replace(os.sep, "/") for p in POSTPROCESSOR_8192):
        return 8192
    size = int(re.search(r"\n  maxTextureSize: (\d+)", meta_text).group(1))
    for block in re.split(r"\n  - serializedVersion: \d+\n", meta_text):
        if "buildTarget: Standalone" in block and re.search(r"\n    overridden: 1", block):
            size = int(re.search(r"maxTextureSize: (\d+)", block).group(1))
    return size


def imported_width(png_path):
    w, h = png_size(png_path)
    with open(png_path + ".meta", encoding="utf-8") as f:
        meta = f.read()
    npot = re.search(r"nPOTScale: (\d+)", meta)
    if npot and npot.group(1) != "0":
        sys.exit(f"{png_path}: nPOTScale={npot.group(1)} не поддерживается (ожидается 0 = None)")
    limit = max_texture_size(png_path, meta)
    if max(w, h) <= limit:
        return w
    if w >= h:
        return limit
    # Ширина — меньшая сторона: Unity масштабирует пропорционально, округление не проверено.
    print(f"warning: {png_path}: {w}x{h} ужат до {limit} по высоте, ширина приблизительна",
          file=sys.stderr)
    return round(w * limit / h)


def resource_key(path_without_ext):
    return os.path.relpath(path_without_ext, RESOURCES).replace(os.sep, "/")


def ordinal(key):
    return key.encode("utf-8")


def main():
    sheets, images = {}, set()
    for dirpath, _, files in os.walk(os.path.join(RESOURCES, "Graphics")):
        names = set(files)
        for name in files:
            full = os.path.join(dirpath, name)
            if name.endswith(".png.json"):
                png_path = full[:-len(".json")]
                if not os.path.exists(png_path):
                    sys.exit(f"нет текстуры для {full}")
                with open(full, encoding="utf-8-sig") as f:
                    cols = int(json.load(f)["Cols"])
                sheets[resource_key(png_path[:-len(".png")])] = {"Width": imported_width(png_path), "Cols": cols}
            elif name.lower().endswith(IMAGE_EXTENSIONS) and name + ".json" not in names:
                images.add(resource_key(os.path.splitext(full)[0]))

    sheet_lines = [f"    {json.dumps(k, ensure_ascii=False)}: {{ \"Width\": {v['Width']}, \"Cols\": {v['Cols']} }}"
                   for k, v in sorted(sheets.items(), key=lambda kv: ordinal(kv[0]))]
    image_lines = [f"    {json.dumps(k, ensure_ascii=False)}" for k in sorted(images, key=ordinal)]
    with open(OUTPUT, "w", encoding="utf-8", newline="\n") as f:
        f.write("{\n")
        f.write('  "_comment": "Сгенерировано tools/graphics-catalog/gen_graphics_catalog.py — не править руками.",\n')
        f.write(f'  "PixelsPerUnit": {PIXELS_PER_UNIT},\n')
        f.write('  "Sheets": {\n' + ",\n".join(sheet_lines) + "\n  },\n")
        f.write('  "Images": [\n' + ",\n".join(image_lines) + "\n  ]\n")
        f.write("}\n")
    print(f"{len(sheets)} листов, {len(images)} картинок → {os.path.relpath(OUTPUT, ROOT)}")


if __name__ == "__main__":
    main()
