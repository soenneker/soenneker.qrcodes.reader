# Regenerate with Python 3, segno==1.6.6, and pypng==0.20220715.0.
# Fixtures are independent of the C# reader and generator implementations.
from pathlib import Path
import struct
import zlib
import png
import segno

root = Path(__file__).parent / 'Fixtures'
root.mkdir(exist_ok=True)
qr = segno.make('PNG fixtures: Hello 世界 🌍', version=5, error='h', encoding='utf-8', eci=True, micro=False)
matrix = [list(row) for row in qr.matrix_iter(scale=2, border=4)]
width = len(matrix)
for color, depths in [('gray', [1, 2, 4, 8, 16]), ('rgb', [8, 16]), ('palette', [1, 2, 4, 8]), ('gray-alpha', [8, 16]), ('rgba', [8, 16])]:
    for depth in depths:
        for interlace in [False, True]:
            maximum = (1 << depth) - 1
            rows = []
            for row in matrix:
                samples = []
                for dark in row:
                    v = 0 if dark else maximum
                    if color == 'gray': samples.append(v)
                    elif color == 'rgb': samples.extend([v, v, v])
                    elif color == 'palette': samples.append(0 if dark else 1)
                    elif color == 'gray-alpha': samples.extend([0, maximum if dark else 0])
                    else: samples.extend([0, 0, 0, maximum if dark else 0])
                rows.append(samples)
            opts = dict(width=width, height=width, bitdepth=depth, interlace=interlace)
            if color == 'palette': opts['palette'] = [(0, 0, 0, 255), (255, 255, 255, 0)]
            else: opts.update(greyscale=color.startswith('gray'), alpha=color in ['gray-alpha', 'rgba'])
            with (root / f'{color}-{depth}-{int(interlace)}.png').open('wb') as output:
                png.Writer(**opts).write(output, rows)

# Independent forward filtering of a known grayscale image exercises every unfilter.
def chunk(kind, data):
    return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data))

def paeth(a, b, c):
    p = a + b - c
    distances = [abs(p-a), abs(p-b), abs(p-c)]
    return [a,b,c][distances.index(min(distances))]

for mode in range(5):
    raw = bytearray()
    previous = [0]*width
    for modules in matrix:
        row = [0 if dark else 255 for dark in modules]
        raw.append(mode)
        for x, value in enumerate(row):
            a = row[x-1] if x else 0
            b = previous[x]
            c = previous[x-1] if x else 0
            prediction = [0, a, b, (a+b)//2, paeth(a,b,c)][mode]
            raw.append((value-prediction)&255)
        previous = row
    header = struct.pack('>IIBBBBB', width, width, 8, 0, 0, 0, 0)
    (root/f'filter-{mode}.png').write_bytes(b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',header)+chunk(b'IDAT',zlib.compress(raw))+chunk(b'IEND',b''))
print(f'Generated {len(list(root.glob("*.png")))} independent PNG fixtures.')

modes = root / 'Modes'
modes.mkdir(exist_ok=True)
segno.make('汉字测试', mode='hanzi', version=4, error='h', micro=False).save(modes/'hanzi.png', scale=3)
sequence = segno.make_sequence('Structured append payload ' * 4, symbol_count=2, error='m')
for index, symbol in enumerate(sequence):
    symbol.save(modes/f'sequence-{index}.png', scale=3)
