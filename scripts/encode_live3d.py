"""Encode the FBX export as flash-resident fixed-point model data."""
from pathlib import Path
import json
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'simulator/test-results/live3d'


def floats(values):
    result = []
    for value in values:
        literal = f'{value:.9g}'
        if '.' not in literal and 'e' not in literal:
            literal += '.0'
        result.append(literal + 'f')
    return ','.join(result)


def main():
    model = json.loads((SOURCE / 'model.json').read_text())
    text = '#pragma once\n#include <cstdint>\nnamespace live_model {\n'
    text += 'struct Vertex { int16_t x,y,z; uint16_t u,v; uint8_t bone[4],weight[4]; };\n'
    text += 'inline constexpr Vertex vertices[]={\n'
    for vertex in model['vertices']:
        fields = ','.join(map(str, vertex[:5]))
        bones = ','.join(map(str, vertex[5:9]))
        weights = ','.join(map(str, vertex[9:13]))
        assert sum(vertex[9:13]) == 255
        text += '{' + fields + ',{' + bones + '},{' + weights + '}},\n'
    text += '};\ninline constexpr uint16_t indices[]={'
    text += ','.join(map(str, model['indices'])) + '};\n'
    text += 'inline constexpr int parents[]={'
    text += ','.join(str(b['parent']) for b in model['bones']) + '};\n'
    for name in ['local', 'inverse']:
        text += f'inline constexpr float {name}[][16]={{\n'
        text += ''.join('{' + floats(b[name]) + '},\n' for b in model['bones']) + '};\n'
    image = Image.open(SOURCE / 'color.jpg').convert('RGB').resize((128, 128), Image.Resampling.LANCZOS)
    pixels = [((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3) for r, g, b in image.getdata()]
    text += 'inline constexpr uint16_t texture[]={' + ','.join(map(str, pixels)) + '};\n'
    small = Image.open(SOURCE / 'color.jpg').convert('RGB').resize((64, 64), Image.Resampling.LANCZOS)
    small_pixels = [((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3) for r, g, b in small.getdata()]
    text += 'inline constexpr uint16_t texture64[]={' + ','.join(map(str, small_pixels)) + '};\n}\n'
    (ROOT / 'firmware/assets/live_model.h').write_text(text)
    # Identify the front screen triangles independently of the low-resolution
    # body atlas. Firmware projects a separate face canvas over this surface.
    face_triangles = []
    for start in range(0, len(model['indices']), 3):
        tri = [model['vertices'][i] for i in model['indices'][start:start+3]]
        center = [sum(v[k] / 4096 for v in tri) / 3 for k in range(3)]
        face_triangles.append(int(-.29 < center[0] < .29 and .43 < center[1] < .96 and center[2] > .16))
    if sum(face_triangles) < 10:
        raise ValueError('Face surface mapping failed')
    header = '#pragma once\n#include <cstdint>\nnamespace live_face {\ninline constexpr uint8_t triangles[]={' + ','.join(map(str, face_triangles)) + '};\n}\n'
    (ROOT / 'firmware/assets/face_lookup.h').write_text(header)
    print(f'Dynamic face mapping: {sum(face_triangles)} screen triangles')
    size = len(model['vertices']) * 18 + len(model['indices']) * 2 + len(pixels) * 2 + len(model['bones']) * 132
    print(f'Model data: {size:,} bytes; {len(model["vertices"])} vertices; {len(model["indices"]) // 3} triangles')


if __name__ == '__main__':
    main()
