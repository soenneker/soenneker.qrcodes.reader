# Soenneker.QrCodes.Reader

**Cross-platform QR code reading in managed C#.** Decode PNG images, raw pixels, or sampled QR grids, with built-in detection and error correction.

[![NuGet version](https://img.shields.io/nuget/v/soenneker.qrcodes.reader.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.qrcodes.reader/)
[![Build and test](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.qrcodes.reader/build-and-test.yml?style=for-the-badge&label=build%20%26%20test)](https://github.com/soenneker/soenneker.qrcodes.reader/actions/workflows/build-and-test.yml)
[![NuGet downloads](https://img.shields.io/nuget/dt/soenneker.qrcodes.reader.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.qrcodes.reader/)

The QR decoder, Reed–Solomon correction, image detector, and PNG reader are implemented in this project using .NET APIs. Production code has no third-party QR or imaging dependency and uses no native graphics APIs.

- **Windows, Linux, and macOS** — targets .NET 11.
- **QR Model 2** — versions 1–40, all correction levels, and all eight masks.
- **Flexible input** — PNG, 8-bit grayscale, packed RGBA, and module grids.
- **Text support** — numeric, alphanumeric, byte, Kanji, Hanzi, and common ECI encodings.
- **Image detection** — rotations, mirroring, inverted colors, local thresholding, and alignment-assisted perspective sampling.
- **Concurrent use** — stateless public service with immutable options and optional dependency injection.

## Installation

```shell
dotnet add package Soenneker.QrCodes.Reader
```

## Quick start

```csharp
using System;
using System.IO;
using Soenneker.QrCodes.Reader;

var reader = new QrCodesReader();
byte[] png = await File.ReadAllBytesAsync("qr.png");
QrCodeReadResult? result = reader.ReadPng(png);

if (result is not null)
{
    Console.WriteLine(result.Text);
    Console.WriteLine($"Version {result.Version}, correction {result.ErrorCorrection}");
}
```

A valid image without a recoverable, supported QR symbol returns `null`. Invalid image containers, dimensions, and options throw an exception. Image methods return the **first decodable symbol**, not every symbol in the image.

## Input formats

| Method | Input | Layout |
| --- | --- | --- |
| `ReadPng` | `ReadOnlySpan<byte>` | Complete PNG file |
| `ReadGrayscale` | Pixels, width, height | One byte per pixel; `0` is black, `255` is white |
| `ReadRgba` | Pixels, width, height | Four bytes per pixel in R, G, B, A order |
| `ReadModules` | `bool[,]` | One module per cell, indexed `[row, column]`; `true` is dark |

Raw pixels must be tightly packed in row order, without padding between rows. Transparent pixels are composited on white. A module grid must exclude the quiet zone and have a valid Model 2 size: 21, 25, 29, …, 177.

```csharp
// Pixels supplied by your camera or imaging pipeline:
QrCodeReadResult? fromPixels = reader.ReadRgba(rgbaPixels, width, height);

// A QR grid that has already been located and sampled:
QrCodeReadResult? fromGrid = reader.ReadModules(modules);
```

All reading methods are synchronous and perform no file or network I/O. The caller owns input buffers and must not modify them while a read is in progress. Reading never modifies the inputs.

### PNG support

The PNG reader handles grayscale, RGB, indexed palettes, grayscale-alpha, and RGBA; all legal bit depths for each color type; all five PNG filters; and Adam7 interlacing. It validates chunk CRCs and the zlib checksum. For APNG files, it reads the default PNG image, not subsequent animation frames.

JPEG, GIF, WebP, and SVG files are not decoded directly. Convert those formats to grayscale or RGBA using your application's imaging pipeline, then pass the pixels to the reader.

## Options

```csharp
var options = new QrCodeReaderOptions
{
    TryInverted = true,
    TryMirrored = true,
    MaxPixels = 16_000_000
};

QrCodeReadResult? result = reader.ReadPng(png, options);
```

| Option | Default | Behavior |
| --- | --- | --- |
| `TryInverted` | `true` | Also attempts light symbols on dark backgrounds |
| `TryMirrored` | `true` | Also attempts mirrored symbols |
| `MaxPixels` | `16,000,000` | Maximum decoded image pixels; accepts `1`–`64,000,000` |

Rotation handling is always enabled. PNG input is additionally limited to **64 MiB** and **32,768 pixels per dimension**. Raw pixel buffers must exactly match their declared dimensions and channel count.

For reliable detection, retain the symbol's quiet zone and sufficient resolution and contrast. The detector handles tested rotations and mild perspective distortion, but severe blur, occlusion, distortion, or clutter can prevent a read. Supplied module grids bypass image detection.

## Decoded results

| Property | Meaning |
| --- | --- |
| `Text` | Decoded text with ECI interpretation and FNC1 separator handling |
| `PayloadBytes` | Concatenated segment bytes, excluding mode and ECI headers |
| `Version` | QR version, from 1 through 40 |
| `ErrorCorrection` | `Low`, `Medium`, `Quartile`, or `High` |
| `CorrectedCodewords` | Number of data or error-correction codewords repaired |
| `IsGs1` | Presence of an FNC1 first-position header |
| `Fnc1ApplicationIndicator` | Application indicator from an FNC1 second-position header, if present |
| `SequenceNumber` | Zero-based structured-append position, if present |
| `SequenceCount` | Structured-append symbol count, if present |
| `SequenceParity` | Structured-append parity byte, if present |

`PayloadBytes` preserves original bytes for byte, Kanji, and Hanzi segments. Numeric and alphanumeric segments contribute ASCII bytes, with FNC1 substitutions applied. Each result has its own payload array.

Byte segments without an ECI marker use **ISO-8859-1**. The reader does not guess UTF-8 when no ECI is declared. Supported ECI encodings include UTF-8, UTF-16 big-endian, ASCII, Shift-JIS, common ISO-8859 and Windows code pages, Big5, GB18030, and EUC-KR. Unsupported encoding assignments return `null`.

Structured-append symbols are returned individually with their metadata; assembling and validating a complete sequence is the caller's responsibility. Micro QR and rMQR are not supported.

## Dependency injection

```csharp
using Soenneker.QrCodes.Reader.Registrars;

builder.Services.AddQrCodesReaderAsSingleton();
```

Inject `IQrCodesReader` from `Soenneker.QrCodes.Reader.Abstract` wherever reading is needed. A singleton supports concurrent calls. Use `AddQrCodesReaderAsScoped()` for one instance per scope. Both registrars preserve an existing registration.

The production package references `Microsoft.Extensions.DependencyInjection.Abstractions` for these registrars.

## Errors

| Condition | Behavior |
| --- | --- |
| No QR symbol, unrecoverable damage, or unsupported QR encoding | Returns `null` |
| Invalid PNG signature, structure, checksum, or compressed data | `InvalidDataException` |
| Invalid dimensions, buffer length, module-grid size, or image-size limit | `ArgumentException` |
| Null module grid | `ArgumentNullException` |
| Invalid option value | `ArgumentOutOfRangeException` |

## Verification

Tests use an independent encoder and cover every version/correction combination at capacity, all masks, image detection across all 40 versions, Reed–Solomon repair up to its correction capacity, text encodings, rotation, mirroring, inversion, perspective, uneven lighting, validation, and concurrency.

PNG fixtures are generated independently with Segno and PyPNG, including color types, bit depths, filters, interlacing, Hanzi, and structured append. The fixture-generation script is included in the test project; Python is not needed to run the tests. ZXing.Net is a test-only dependency.

CI is configured to build and test on Windows, Linux, and macOS.

## License

[MIT](LICENSE)
