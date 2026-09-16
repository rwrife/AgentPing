"""Build an original Teams-style demo glyph in the robot's 48x48 bit format."""
from pathlib import Path


def pixels():
    for y in range(48):
        for x in range(48):
            # Two people behind an outlined tile with a bold T.
            head = (x - 29) ** 2 + (y - 10) ** 2 <= 25
            head |= (x - 40) ** 2 + (y - 14) ** 2 <= 16
            body = 24 <= x <= 35 and 19 <= y <= 37
            body |= 37 <= x <= 44 and 23 <= y <= 33
            tile = 3 <= x <= 27 and 17 <= y <= 41
            border = tile and (x <= 5 or x >= 25 or y <= 19 or y >= 39)
            letter = 9 <= x <= 21 and 23 <= y <= 25
            letter |= 14 <= x <= 16 and 24 <= y <= 35
            yield x, y, (border or letter) if tile else (head or body)


def main():
    target = Path(__file__).resolve().parents[1] / 'assets/icons'
    data = bytearray(288)
    rects = []
    for x, y, ink in pixels():
        if ink:
            data[y * 6 + x // 8] |= 1 << (x % 8)
            rects.append(f'<rect x="{x}" y="{y}" width="1" height="1"/>')
    (target / 'teams-48.bin').write_bytes(data)
    (target / 'teams-48.svg').write_text(
        '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 48 48" '
        'shape-rendering="crispEdges"><title>Teams-style demo icon</title>'
        '<g fill="#8b88ff">' + ''.join(rects) + '</g></svg>\n')


if __name__ == '__main__':
    main()
