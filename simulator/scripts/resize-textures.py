from pathlib import Path
from PIL import Image
source = Path('public/character/rigged')
target = Path('public/character/optimized')
target.mkdir(parents=True, exist_ok=True)
for suffix in ['', '_normal', '_roughness', '_metallic']:
    image = Image.open(source / ('Meshy_AI_Pixel_Pal_biped_texture_0' + suffix + '.png'))
    image.thumbnail((512, 512), Image.Resampling.LANCZOS)
    image.save(target / ('texture' + suffix + '.png'), optimize=True)
