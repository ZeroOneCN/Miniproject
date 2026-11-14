#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""把 assets/icon.svg 渲染成 PNG 与多尺寸 ICO。

用法：
    python tools/build_icons.py
"""

from pathlib import Path

from PIL import Image
from PyQt6.QtCore import QByteArray, QRectF, Qt
from PyQt6.QtGui import QGuiApplication, QImage, QPainter
from PyQt6.QtSvg import QSvgRenderer

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / "assets"
SVG = ASSETS / "icon.svg"

ICO_SIZES = (16, 24, 32, 48, 64, 128, 256)
MASTER = 512


def render_svg(size: int) -> Image.Image:
    """用 Qt 把 SVG 渲染成指定边长的 RGB 位图。"""
    renderer = QSvgRenderer(QByteArray(SVG.read_bytes()))
    image = QImage(size, size, QImage.Format.Format_ARGB32)
    image.fill(Qt.GlobalColor.transparent)
    painter = QPainter(image)
    painter.setRenderHint(QPainter.RenderHint.Antialiasing, True)
    painter.setRenderHint(QPainter.RenderHint.SmoothPixmapTransform, True)
    renderer.render(painter, QRectF(0, 0, size, size))
    painter.end()

    image = image.convertToFormat(QImage.Format.Format_RGBA8888)
    buffer = bytes(image.constBits().asstring(image.sizeInBytes()))
    return Image.frombytes("RGBA", (size, size), buffer)


def main() -> None:
    if not SVG.exists():
        raise SystemExit(f"找不到图标源文件: {SVG}")

    ASSETS.mkdir(parents=True, exist_ok=True)
    _ = QGuiApplication.instance() or QGuiApplication([])

    master = render_svg(MASTER)
    master.save(ASSETS / "icon.png")
    master.resize((256, 256), Image.LANCZOS).save(ASSETS / "icon_256.png")

    frames = [render_svg(size).convert("RGBA") for size in ICO_SIZES]
    frames[-1].save(ASSETS / "icon.ico", format="ICO", sizes=[(s, s) for s in ICO_SIZES])

    for name in ("icon.png", "icon_256.png", "icon.ico"):
        path = ASSETS / name
        print(f"生成 {path.relative_to(ROOT)}  {path.stat().st_size / 1024:.1f} KB")


if __name__ == "__main__":
    main()
