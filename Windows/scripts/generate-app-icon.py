from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[2]
OUTPUT_DIRECTORY = ROOT / "Resources"
CANVAS_SIZE = 1024


def draw_round_line(
    draw: ImageDraw.ImageDraw,
    start: tuple[int, int],
    end: tuple[int, int],
    color: str,
    width: int,
) -> None:
    radius = width // 2
    draw.line([start, end], fill=color, width=width)
    for x, y in (start, end):
        draw.ellipse(
            (x - radius, y - radius, x + radius, y + radius),
            fill=color,
        )


def main() -> None:
    OUTPUT_DIRECTORY.mkdir(parents=True, exist_ok=True)
    image = Image.new("RGBA", (CANVAS_SIZE, CANVAS_SIZE), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)

    draw.rounded_rectangle(
        (64, 64, 960, 960),
        radius=238,
        fill="#0B1117",
        outline="#293640",
        width=8,
    )

    stroke_width = 136
    draw_round_line(draw, (286, 304), (512, 736), "#FF6845", stroke_width)
    draw_round_line(draw, (512, 736), (738, 304), "#38D989", stroke_width)

    preview = image.resize((512, 512), Image.Resampling.LANCZOS)
    preview.save(OUTPUT_DIRECTORY / "VibeControl-icon.png", format="PNG")
    preview.save(
        OUTPUT_DIRECTORY / "VibeControl.ico",
        format="ICO",
        sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)],
        bitmap_format="png",
    )


if __name__ == "__main__":
    main()
