---
title: Documentation - CodeGlyphX
description: CodeGlyphX documentation - learn how to generate and decode QR codes, barcodes, and 2D matrix codes in .NET.
slug: index
collection: docs
layout: docs
meta.raw_html: true
meta.extra_scripts_file: index.scripts.html
---

<div class="docs-layout">
    <button class="docs-sidebar-toggle" aria-label="Toggle documentation menu">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" width="20" height="20">
            <path d="M4 6h16M4 12h16M4 18h16"/>
        </svg>
        <span>Documentation Menu</span>
    </button>
    <div class="docs-sidebar-overlay"></div>
    <aside class="docs-sidebar">
        <nav class="docs-nav">
            <div class="docs-nav-section">
                <div class="docs-nav-title">Getting Started</div>
                <a href="#introduction" class="active">Introduction</a>
                <a href="#installation">Installation</a>
                <a href="#quickstart">Quick Start</a>
            </div>

            <div class="docs-nav-section">
                <div class="docs-nav-title">2D Codes</div>
                <a href="#qr">QR Code</a>
                <a href="#microqr">Micro QR</a>
                <a href="#datamatrix">Data Matrix</a>
                <a href="#pdf417">PDF417</a>
                <a href="#aztec">Aztec</a>
            </div>

            <div class="docs-nav-section">
                <div class="docs-nav-title">1D Barcodes</div>
                <a href="#code128">Code 128 / GS1-128</a>
                <a href="#code39">Code 39 / 93</a>
                <a href="#ean-upc">EAN / UPC</a>
            </div>

            <div class="docs-nav-section">
                <div class="docs-nav-title">Features</div>
                <a href="#payloads">Payload Helpers</a>
                <a href="#decoding">Image Decoding</a>
                <a href="#renderers">Output Formats</a>
                <a href="#benchmarks">Benchmarks</a>
            </div>

            <div class="docs-nav-section">
                <div class="docs-nav-title">Reference</div>
                <a href="/docs/api/">API Reference &rarr;</a>
            </div>
        </nav>
    </aside>

    <div class="docs-content">
        {{< edit-link >}}

        <section id="introduction">
            <h1>CodeGlyphX Documentation</h1>
            <p>
                Welcome to the CodeGlyphX documentation. CodeGlyphX is a zero-dependency .NET library
                for generating and decoding QR codes, barcodes, and other 2D matrix codes.
            </p>

            <h2>Key Features</h2>
            <ul style="color: var(--text-muted); margin-left: 1.5rem;">
                <li><strong>Zero external dependencies</strong> - No System.Drawing, SkiaSharp, or ImageSharp required</li>
                <li><strong>Full encode &amp; decode</strong> - Round-trip support for all symbologies</li>
                <li><strong>Multiple output formats</strong> - PNG, SVG, PDF, EPS, HTML, and many more</li>
                <li><strong>Cross-platform</strong> - Windows, Linux, macOS</li>
                <li><strong>AOT compatible</strong> - Works with Native AOT and trimming</li>
            </ul>

            <h2>Supported Symbologies</h2>
            <h3>2D Matrix Codes</h3>
            <p>QR Code, Micro QR, Data Matrix, PDF417, Aztec</p>

            <h3>1D Linear Barcodes</h3>
            <p>Code 128, GS1-128, Code 39, Code 93, Code 11, Codabar, MSI, Plessey, EAN-8, EAN-13, UPC-A, UPC-E, ITF-14</p>

            <h2>Quick Example</h2>
            <pre class="code-block">using CodeGlyphX;

// Generate a QR code
QR.Save("https://evotec.xyz", "website.png");

// Generate a barcode
Barcode.Save(BarcodeType.Code128, "PRODUCT-123", "barcode.png");

// Decode an image
if (QrImageDecoder.TryDecodeImage(imageBytes, out var result))
{
    Console.WriteLine(result.Text);
}</pre>

            <h2>Getting Help</h2>
            <p>
                If you encounter issues or have questions, please visit the
                <a href="https://github.com/EvotecIT/CodeGlyphX/issues" target="_blank">GitHub Issues</a> page.
            </p>
        </section>

        <section id="installation">
            <h1>Installation</h1>
            <p>CodeGlyphX is available as a NuGet package and can be installed in several ways.</p>

            <h2>.NET CLI</h2>
            <pre class="code-block">dotnet add package CodeGlyphX</pre>

            <h2>Package Manager Console</h2>
            <pre class="code-block">Install-Package CodeGlyphX</pre>

            <h2>PackageReference</h2>
            <p>Add the following to your <code>.csproj</code> file:</p>
            <pre class="code-block">&lt;PackageReference Include="CodeGlyphX" Version="*" /&gt;</pre>

            <h2>Supported Frameworks</h2>
            <ul style="color: var(--text-muted); margin-left: 1.5rem;">
                <li><strong>.NET 8.0+</strong> - Full support, no additional dependencies</li>
                <li><strong>.NET Standard 2.0</strong> - Requires System.Memory 4.5.5</li>
                <li><strong>.NET Framework 4.7.2+</strong> - Requires System.Memory 4.5.5</li>
            </ul>

            <h2>Feature Availability</h2>
            <p>Most features are available across all targets, but the QR pixel pipeline and Span-based APIs are net8+ only.</p>
            <table style="width: 100%; border-collapse: collapse; margin: 1rem 0;">
                <thead>
                    <tr style="border-bottom: 1px solid var(--border);">
                        <th style="text-align: left; padding: 0.75rem;">Feature</th>
                        <th style="text-align: left; padding: 0.75rem;">net8.0+</th>
                        <th style="text-align: left; padding: 0.75rem;">net472 / netstandard2.0</th>
                    </tr>
                </thead>
                <tbody style="color: var(--text-muted);">
                    <tr style="border-bottom: 1px solid var(--border);">
                        <td style="padding: 0.75rem;">Encode (QR/Micro QR + 1D/2D)</td>
                        <td style="padding: 0.75rem;">Yes</td>
                        <td style="padding: 0.75rem;">Yes</td>
                    </tr>
                    <tr style="border-bottom: 1px solid var(--border);">
                        <td style="padding: 0.75rem;">Decode from module grids (BitMatrix)</td>
                        <td style="padding: 0.75rem;">Yes</td>
                        <td style="padding: 0.75rem;">Yes</td>
                    </tr>
                    <tr style="border-bottom: 1px solid var(--border);">
                        <td style="padding: 0.75rem;">Renderers + image file codecs</td>
                        <td style="padding: 0.75rem;">Yes</td>
                        <td style="padding: 0.75rem;">Yes</td>
                    </tr>
                    <tr style="border-bottom: 1px solid var(--border);">
                        <td style="padding: 0.75rem;">1D/2D pixel decode (Barcode/DataMatrix/PDF417/Aztec)</td>
                        <td style="padding: 0.75rem;">Yes</td>
                        <td style="padding: 0.75rem;">Yes</td>
                    </tr>
                    <tr style="border-bottom: 1px solid var(--border);">
                        <td style="padding: 0.75rem;">QR pixel decode from raw pixels / screenshots</td>
                        <td style="padding: 0.75rem;">Yes</td>
                        <td style="padding: 0.75rem;">No (returns false)</td>
                    </tr>
                    <tr style="border-bottom: 1px solid var(--border);">
                        <td style="padding: 0.75rem;">QR pixel debug rendering</td>
                        <td style="padding: 0.75rem;">Yes</td>
                        <td style="padding: 0.75rem;">No</td>
                    </tr>
                    <tr>
                        <td style="padding: 0.75rem;">Span-based overloads</td>
                        <td style="padding: 0.75rem;">Yes</td>
                        <td style="padding: 0.75rem;">No (byte[] only)</td>
                    </tr>
                </tbody>
            </table>
            <p class="text-muted">QR pixel decode APIs are net8+ only (e.g., <code>QrImageDecoder.TryDecodeImage(...)</code> and <code>QrDecoder.TryDecode(...)</code> from pixels).</p>
            <p class="text-muted">You can check capabilities at runtime via <code>CodeGlyphXFeatures</code> (for example, <code>SupportsQrPixelDecode</code> and <code>SupportsQrPixelDebug</code>).</p>
            <p class="text-muted"><strong>Choosing a target:</strong> pick <code>net8.0+</code> for QR image decoding, pixel debug tools, Span APIs, and maximum throughput. Pick <code>net472</code>/<code>netstandard2.0</code> for legacy apps that only need encoding, rendering, and module-grid decode.</p>
        </section>

        <section id="quickstart">
            <h1>Quick Start</h1>
            <p>Get up and running with CodeGlyphX in under a minute.</p>

            <h2>1. Install the Package</h2>
            <pre class="code-block">dotnet add package CodeGlyphX</pre>

            <h2>2. Generate Your First QR Code</h2>
            <pre class="code-block">using CodeGlyphX;

// Create a QR code and save to file
QR.Save("Hello, World!", "hello.png");

// The output format is determined by the file extension
QR.Save("Hello, World!", "hello.svg");  // Vector SVG
QR.Save("Hello, World!", "hello.pdf");  // PDF document</pre>

            <h2>3. Generate Barcodes</h2>
            <pre class="code-block">using CodeGlyphX;

// Code 128 barcode
Barcode.Save(BarcodeType.Code128, "PRODUCT-12345", "barcode.png");

// EAN-13 (retail products)
Barcode.Save(BarcodeType.Ean13, "5901234123457", "ean.png");</pre>

            <h2>4. Decode Images</h2>
            <pre class="code-block">using CodeGlyphX;

var imageBytes = File.ReadAllBytes("qrcode.png");

if (QrImageDecoder.TryDecodeImage(imageBytes, out var result))
{
    Console.WriteLine($"Decoded: {result.Text}");
}</pre>
        </section>

        <section id="benchmarks">
            <h1>Benchmarks</h1>
            <p>
                Benchmarks are run locally using BenchmarkDotNet under controlled, ideal conditions. Results below were captured on
                2026-01-18 (Ubuntu 24.04, Ryzen 9 9950X, .NET 8.0.22). Treat these numbers as directional; your results will vary.
            </p>
            <p class="text-muted">Quick runs use fewer iterations but include the same scenario list as full runs. See <code>BENCHMARK.md</code> for full tables.</p>
            <div class="bench-summary" data-benchmark-summary>
                <div class="bench-summary-loading">Loading benchmark summary...</div>
            </div>

            <h2>QR (Encode)</h2>
            <table style="width: 100%; border-collapse: collapse; margin: 1rem 0;">
                <thead>
                    <tr style="border-bottom: 1px solid var(--border);">
                        <th style="text-align: left; padding: 0.75rem;">Scenario</th>
                        <th style="text-align: left; padding: 0.75rem;">Mean (us)</th>
                        <th style="text-align: left; padding: 0.75rem;">Allocated</th>
                    </tr>
                </thead>
                <tbody style="color: var(--text-muted);">
                    <tr style="border-bottom: 1px solid var(--border);">
                        <td style="padding: 0.75rem;">QR PNG (short)</td>
                        <td style="padding: 0.75rem;">331.33</td>
                        <td style="padding: 0.75rem;">431.94 KB</td>
                    </tr>
                    <tr style="border-bottom: 1px solid var(--border);">
                        <td style="padding: 0.75rem;">QR PNG (medium)</td>
                        <td style="padding: 0.75rem;">713.68</td>
                        <td style="padding: 0.75rem;">837.75 KB</td>
                    </tr>
                    <tr>
                        <td style="padding: 0.75rem;">QR PNG (long)</td>
                        <td style="padding: 0.75rem;">2197.99</td>
                        <td style="padding: 0.75rem;">3041.06 KB</td>
                    </tr>
                </tbody>
            </table>

            <h2>Run the Benchmarks</h2>
            <pre class="code-block">dotnet run -c Release --framework net8.0 --project CodeGlyphX.Benchmarks/CodeGlyphX.Benchmarks.csproj -- --filter "*"</pre>
        </section>

        <section id="qr">
            <h1>QR Code Generation</h1>
            <p>CodeGlyphX provides comprehensive QR code support including standard QR and Micro QR formats.</p>

            <h2>Basic Usage</h2>
            <pre class="code-block">using CodeGlyphX;

// Simple one-liner
QR.Save("https://example.com", "qr.png");

// With error correction level
QR.Save("https://example.com", "qr.png", QrErrorCorrection.H);</pre>

            <h2>Error Correction Levels</h2>
            <table style="width: 100%; border-collapse: collapse; margin: 1rem 0;">
                <thead>
                    <tr style="border-bottom: 1px solid var(--border);">
                        <th style="text-align: left; padding: 0.75rem;">Level</th>
                        <th style="text-align: left; padding: 0.75rem;">Recovery</th>
                        <th style="text-align: left; padding: 0.75rem;">Use Case</th>
                    </tr>
                </thead>
                <tbody style="color: var(--text-muted);">
                    <tr style="border-bottom: 1px solid var(--border);">
                        <td style="padding: 0.75rem;"><code>L</code></td>
                        <td style="padding: 0.75rem;">~7%</td>
                        <td style="padding: 0.75rem;">Maximum data capacity</td>
                    </tr>
                    <tr style="border-bottom: 1px solid var(--border);">
                        <td style="padding: 0.75rem;"><code>M</code></td>
                        <td style="padding: 0.75rem;">~15%</td>
                        <td style="padding: 0.75rem;">Default, balanced</td>
                    </tr>
                    <tr style="border-bottom: 1px solid var(--border);">
                        <td style="padding: 0.75rem;"><code>Q</code></td>
                        <td style="padding: 0.75rem;">~25%</td>
                        <td style="padding: 0.75rem;">Higher reliability</td>
                    </tr>
                    <tr>
                        <td style="padding: 0.75rem;"><code>H</code></td>
                        <td style="padding: 0.75rem;">~30%</td>
                        <td style="padding: 0.75rem;">Maximum error correction</td>
                    </tr>
                </tbody>
            </table>

            <h2>Styling Options</h2>
            <pre class="code-block">using CodeGlyphX;

var options = new QrEasyOptions
{
    ModuleShape = QrPngModuleShape.Rounded,
    ModuleCornerRadiusPx = 3,
    Eyes = new QrPngEyeOptions
    {
        UseFrame = true,
        OuterShape = QrPngModuleShape.Circle,
        InnerShape = QrPngModuleShape.Circle,
        OuterColor = new Rgba32(220, 20, 60),
        InnerColor = new Rgba32(220, 20, 60)
    }
};

QR.Save("https://example.com", "styled-qr.png", options);</pre>

            <h3>Fluent Builder (logo + styling)</h3>
            <pre class="code-block">using CodeGlyphX;
using CodeGlyphX.Rendering;

var logo = LogoBuilder.CreateCirclePng(
    size: 96,
    color: new Rgba32(24, 24, 24),
    accent: new Rgba32(240, 240, 240),
    out _,
    out _);

var png = QR.Create("https://example.com")
    .WithLogoPng(logo)
    .WithLogoScale(0.22)
    .WithLogoPaddingPx(6)
    .WithStyle(QrRenderStyle.Fancy)
    .Png();</pre>
        </section>

        <section id="payloads">
            <h1>Payload Helpers</h1>
            <p>CodeGlyphX includes built-in helpers for generating QR codes with structured payloads that mobile devices can interpret.</p>

            <h2>WiFi Configuration</h2>
            <pre class="code-block">using CodeGlyphX;
using CodeGlyphX.Payloads;

// WPA/WPA2 network
QR.Save(QrPayloads.Wifi("NetworkName", "Password123"), "wifi.png");

// Open network (no password)
QR.Save(QrPayloads.Wifi("PublicNetwork", null, WifiAuthType.None), "wifi-open.png");</pre>

            <h2>Contact Cards</h2>
            <pre class="code-block">// vCard format (widely supported)
QR.Save(QrPayloads.VCard(
    firstName: "Przemyslaw",
    lastName: "Klys",
    email: "contact@evotec.pl",
    phone: "+48123456789",
    website: "https://evotec.xyz",
    organization: "Evotec Services"
), "contact.png");</pre>

            <h2>OTP / 2FA</h2>
            <pre class="code-block">// TOTP (Time-based One-Time Password)
QR.Save(QrPayloads.OneTimePassword(
    OtpAuthType.Totp,
    secret: "JBSWY3DPEHPK3PXP",
    label: "user@example.com",
    issuer: "MyApp"
), "totp.png");</pre>

            <h2>SEPA Girocode</h2>
            <pre class="code-block">QR.Save(QrPayloads.Girocode(
    iban: "DE89370400440532013000",
    bic: "COBADEFFXXX",
    recipientName: "Evotec Services",
    amount: 99.99m,
    reference: "Invoice-2024-001"
), "sepa.png");</pre>
        </section>

        <section id="renderers">
            <h1>Output Formats</h1>
            <p>CodeGlyphX supports a wide variety of output formats. The format is automatically determined by the file extension.</p>

            <h2>Vector Formats</h2>
            <ul style="color: var(--text-muted); margin-left: 1.5rem;">
                <li><strong>SVG</strong> (<code>.svg</code>) - Scalable, web-friendly</li>
                <li><strong>SVGZ</strong> (<code>.svgz</code>) - Compressed SVG</li>
                <li><strong>PDF</strong> (<code>.pdf</code>) - Vector by default</li>
                <li><strong>EPS</strong> (<code>.eps</code>) - PostScript</li>
            </ul>

            <h2>Raster Formats</h2>
            <ul style="color: var(--text-muted); margin-left: 1.5rem;">
                <li><strong>PNG</strong> (<code>.png</code>) - Lossless, transparent</li>
                <li><strong>JPEG</strong> (<code>.jpg</code>) - Lossy compression</li>
                <li><strong>BMP</strong> (<code>.bmp</code>) - Uncompressed bitmap</li>
                <li><strong>TGA</strong> (<code>.tga</code>) - Targa format</li>
                <li><strong>ICO</strong> (<code>.ico</code>) - Windows icon</li>
            </ul>

            <h2>Special Formats</h2>
            <ul style="color: var(--text-muted); margin-left: 1.5rem;">
                <li><strong>HTML</strong> (<code>.html</code>) - Table-based output</li>
                <li><strong>PPM/PBM/PGM/PAM</strong> - Portable pixel formats</li>
                <li><strong>XBM/XPM</strong> - X Window formats</li>
                <li><strong>ASCII</strong> - Text representation (API only)</li>
            </ul>

            <h2>Programmatic Rendering</h2>
            <pre class="code-block">using CodeGlyphX;

// Get raw PNG bytes
byte[] pngBytes = QrEasy.RenderPng("Hello", QrErrorCorrection.M, moduleSize: 10);

// Get SVG string
string svg = QrEasy.RenderSvg("Hello", QrErrorCorrection.M);

// Get Base64 data URI
string dataUri = QrEasy.RenderPngBase64DataUri("Hello");</pre>
        </section>

        <section id="datamatrix">
            <h1>Data Matrix</h1>
            <p>Data Matrix is a 2D barcode widely used in industrial and commercial applications for marking small items.</p>

            <h2>Basic Usage</h2>
            <pre class="code-block">using CodeGlyphX;

// Simple Data Matrix
DataMatrixCode.Save("SERIAL-12345", "datamatrix.png");

// With specific size
DataMatrixCode.Save("SERIAL-12345", "datamatrix.svg", size: DataMatrixSize.Square24);</pre>

            <h2>Use Cases</h2>
            <ul style="color: var(--text-muted); margin-left: 1.5rem;">
                <li><strong>Electronics manufacturing</strong> - Component marking and tracking</li>
                <li><strong>Healthcare</strong> - Medical device identification (UDI)</li>
                <li><strong>Aerospace</strong> - Part serialization</li>
                <li><strong>Postal services</strong> - High-density mail sorting</li>
            </ul>

            <h2>Features</h2>
            <p>CodeGlyphX supports all standard Data Matrix sizes from 10x10 to 144x144 modules, including rectangular variants.</p>
        </section>

        <section id="pdf417">
            <h1>PDF417</h1>
            <p>PDF417 is a stacked linear barcode capable of encoding large amounts of data, commonly used in ID cards and transport tickets.</p>

            <h2>Basic Usage</h2>
            <pre class="code-block">using CodeGlyphX;

// Simple PDF417
Pdf417Code.Save("Document content here", "pdf417.png");

// With error correction level
Pdf417Code.Save("Document content", "pdf417.png", errorCorrectionLevel: 5);</pre>

            <h2>Use Cases</h2>
            <ul style="color: var(--text-muted); margin-left: 1.5rem;">
                <li><strong>Government IDs</strong> - Driver's licenses, ID cards</li>
                <li><strong>Travel documents</strong> - Boarding passes, tickets</li>
                <li><strong>Shipping</strong> - Package labels with detailed info</li>
                <li><strong>Inventory</strong> - Large data capacity for detailed records</li>
            </ul>

            <h2>Data Capacity</h2>
            <p>PDF417 can encode up to 1,850 alphanumeric characters or 2,710 numeric digits.</p>
        </section>

        <section id="aztec">
            <h1>Aztec Code</h1>
            <p>Aztec is a 2D matrix barcode designed for high readability even when printed at low resolution or on curved surfaces.</p>

            <h2>Basic Usage</h2>
            <pre class="code-block">using CodeGlyphX;

// Simple Aztec code
AztecCode.Save("Ticket: CONF-2024-001", "aztec.png");

// With error correction percentage
AztecCode.Save("Ticket data", "aztec.png", errorCorrectionPercent: 33);</pre>

            <h2>Use Cases</h2>
            <ul style="color: var(--text-muted); margin-left: 1.5rem;">
                <li><strong>Transportation</strong> - Train and airline tickets</li>
                <li><strong>Event tickets</strong> - Mobile ticketing apps</li>
                <li><strong>Patient wristbands</strong> - Healthcare identification</li>
                <li><strong>Curved surfaces</strong> - Bottles, tubes, cylinders</li>
            </ul>

            <h2>Advantages</h2>
            <p>Aztec codes don't require a quiet zone around them and can be read even when partially damaged, making them ideal for mobile ticketing.</p>
        </section>

        <section id="code128">
            <h1>Code 128 / GS1-128</h1>
            <p>Code 128 is a high-density linear barcode supporting the full ASCII character set. GS1-128 is an application standard that uses Code 128.</p>

            <h2>Basic Usage</h2>
            <pre class="code-block">using CodeGlyphX;

// Code 128
Barcode.Save(BarcodeType.Code128, "PRODUCT-12345", "code128.png");

// GS1-128 with Application Identifiers
Barcode.Save(BarcodeType.Gs1128, "(01)09501101530003(17)250101", "gs1.png");</pre>

            <h2>Character Sets</h2>
            <table style="width: 100%; border-collapse: collapse; margin: 1rem 0;">
                <thead>
                    <tr style="border-bottom: 1px solid var(--border);">
                        <th style="text-align: left; padding: 0.75rem;">Set</th>
                        <th style="text-align: left; padding: 0.75rem;">Characters</th>
                        <th style="text-align: left; padding: 0.75rem;">Best For</th>
                    </tr>
                </thead>
                <tbody style="color: var(--text-muted);">
                    <tr style="border-bottom: 1px solid var(--border);">
                        <td style="padding: 0.75rem;"><code>A</code></td>
                        <td style="padding: 0.75rem;">A-Z, 0-9, control chars</td>
                        <td style="padding: 0.75rem;">Alphanumeric with controls</td>
                    </tr>
                    <tr style="border-bottom: 1px solid var(--border);">
                        <td style="padding: 0.75rem;"><code>B</code></td>
                        <td style="padding: 0.75rem;">A-Z, a-z, 0-9, symbols</td>
                        <td style="padding: 0.75rem;">General text (most common)</td>
                    </tr>
                    <tr>
                        <td style="padding: 0.75rem;"><code>C</code></td>
                        <td style="padding: 0.75rem;">00-99 (digit pairs)</td>
                        <td style="padding: 0.75rem;">Numeric data (most compact)</td>
                    </tr>
                </tbody>
            </table>
        </section>

        <section id="code39">
            <h1>Code 39 / Code 93</h1>
            <p>Code 39 and Code 93 are widely used linear barcodes, particularly in automotive and defense industries.</p>

            <h2>Basic Usage</h2>
            <pre class="code-block">using CodeGlyphX;

// Code 39
Barcode.Save(BarcodeType.Code39, "HELLO-123", "code39.png");

// Code 93 (more compact)
Barcode.Save(BarcodeType.Code93, "HELLO-123", "code93.png");</pre>

            <h2>Valid Characters</h2>
            <p>Code 39 supports: <code>A-Z</code>, <code>0-9</code>, <code>-</code>, <code>.</code>, <code>$</code>, <code>/</code>, <code>+</code>, <code>%</code>, <code>SPACE</code></p>
            <p style="margin-top: 0.5rem;"><strong>Note:</strong> Lowercase letters are automatically converted to uppercase.</p>

            <h2>Comparison</h2>
            <table style="width: 100%; border-collapse: collapse; margin: 1rem 0;">
                <thead>
                    <tr style="border-bottom: 1px solid var(--border);">
                        <th style="text-align: left; padding: 0.75rem;">Feature</th>
                        <th style="text-align: left; padding: 0.75rem;">Code 39</th>
                        <th style="text-align: left; padding: 0.75rem;">Code 93</th>
                    </tr>
                </thead>
                <tbody style="color: var(--text-muted);">
                    <tr style="border-bottom: 1px solid var(--border);">
                        <td style="padding: 0.75rem;">Density</td>
                        <td style="padding: 0.75rem;">Lower</td>
                        <td style="padding: 0.75rem;">~40% more compact</td>
                    </tr>
                    <tr style="border-bottom: 1px solid var(--border);">
                        <td style="padding: 0.75rem;">Checksum</td>
                        <td style="padding: 0.75rem;">Optional</td>
                        <td style="padding: 0.75rem;">Mandatory (2 chars)</td>
                    </tr>
                    <tr>
                        <td style="padding: 0.75rem;">Industry</td>
                        <td style="padding: 0.75rem;">Automotive, Defense</td>
                        <td style="padding: 0.75rem;">Logistics, Postal</td>
                    </tr>
                </tbody>
            </table>
        </section>

        <section id="ean-upc">
            <h1>EAN / UPC Barcodes</h1>
            <p>EAN (European Article Number) and UPC (Universal Product Code) are the standard retail barcodes found on consumer products worldwide.</p>

            <h2>Basic Usage</h2>
            <pre class="code-block">using CodeGlyphX;

// EAN-13 (International)
Barcode.Save(BarcodeType.Ean13, "5901234123457", "ean13.png");

// EAN-8 (Smaller packages)
Barcode.Save(BarcodeType.Ean8, "96385074", "ean8.png");

// UPC-A (North America)
Barcode.Save(BarcodeType.UpcA, "012345678905", "upca.png");

// UPC-E (Compact)
Barcode.Save(BarcodeType.UpcE, "01234565", "upce.png");</pre>

            <h2>Format Guide</h2>
            <table style="width: 100%; border-collapse: collapse; margin: 1rem 0;">
                <thead>
                    <tr style="border-bottom: 1px solid var(--border);">
                        <th style="text-align: left; padding: 0.75rem;">Type</th>
                        <th style="text-align: left; padding: 0.75rem;">Digits</th>
                        <th style="text-align: left; padding: 0.75rem;">Region</th>
                        <th style="text-align: left; padding: 0.75rem;">Use Case</th>
                    </tr>
                </thead>
                <tbody style="color: var(--text-muted);">
                    <tr style="border-bottom: 1px solid var(--border);">
                        <td style="padding: 0.75rem;">EAN-13</td>
                        <td style="padding: 0.75rem;">13</td>
                        <td style="padding: 0.75rem;">International</td>
                        <td style="padding: 0.75rem;">Standard retail products</td>
                    </tr>
                    <tr style="border-bottom: 1px solid var(--border);">
                        <td style="padding: 0.75rem;">EAN-8</td>
                        <td style="padding: 0.75rem;">8</td>
                        <td style="padding: 0.75rem;">International</td>
                        <td style="padding: 0.75rem;">Small packages</td>
                    </tr>
                    <tr style="border-bottom: 1px solid var(--border);">
                        <td style="padding: 0.75rem;">UPC-A</td>
                        <td style="padding: 0.75rem;">12</td>
                        <td style="padding: 0.75rem;">North America</td>
                        <td style="padding: 0.75rem;">Retail products</td>
                    </tr>
                    <tr>
                        <td style="padding: 0.75rem;">UPC-E</td>
                        <td style="padding: 0.75rem;">8</td>
                        <td style="padding: 0.75rem;">North America</td>
                        <td style="padding: 0.75rem;">Small items</td>
                    </tr>
                </tbody>
            </table>
        </section>

        <section id="decoding">
            <h1>Image Decoding</h1>
            <p>CodeGlyphX includes a built-in decoder for reading QR codes and barcodes from images.</p>

            <h2>Basic Usage</h2>
            <pre class="code-block">using CodeGlyphX;

// Decode from file
byte[] imageBytes = File.ReadAllBytes("qrcode.png");

if (QrImageDecoder.TryDecodeImage(imageBytes, out var result))
{
    Console.WriteLine($"Decoded: {result.Text}");
    Console.WriteLine($"Format: {result.BarcodeFormat}");
}

// Decode from stream
using var stream = File.OpenRead("barcode.png");
var decodeResult = QrImageDecoder.DecodeImage(stream);</pre>

            <h2>Supported Formats for Decoding</h2>
            <ul style="color: var(--text-muted); margin-left: 1.5rem;">
                <li><strong>Images:</strong> PNG, JPEG, BMP, GIF</li>
                <li><strong>QR Codes:</strong> Standard QR, Micro QR</li>
                <li><strong>1D Barcodes:</strong> Code 128, Code 39, EAN, UPC</li>
            </ul>

            <h2>Handling Multiple Results</h2>
            <pre class="code-block">// Decode all barcodes in an image
var results = QrImageDecoder.DecodeAllImages(imageBytes);

foreach (var barcode in results)
{
    Console.WriteLine($"{barcode.BarcodeFormat}: {barcode.Text}");
}</pre>

            <h2>Diagnostics</h2>
            <pre class="code-block">using CodeGlyphX;

if (!CodeGlyph.TryDecodeImage(imageBytes, out var decoded, out var diagnostics, options: null))
{
    Console.WriteLine(diagnostics.FailureReason);
    Console.WriteLine(diagnostics.Failure);
}</pre>
        </section>

        <section id="microqr">
            <h1>Micro QR Code</h1>
            <p>Micro QR is a smaller version of the standard QR code, designed for applications where space is limited.</p>

            <h2>Basic Usage</h2>
            <pre class="code-block">using CodeGlyphX;

// Generate Micro QR
QR.Save("ABC123", "microqr.png", microQr: true);</pre>

            <h2>Comparison with Standard QR</h2>
            <table style="width: 100%; border-collapse: collapse; margin: 1rem 0;">
                <thead>
                    <tr style="border-bottom: 1px solid var(--border);">
                        <th style="text-align: left; padding: 0.75rem;">Feature</th>
                        <th style="text-align: left; padding: 0.75rem;">Standard QR</th>
                        <th style="text-align: left; padding: 0.75rem;">Micro QR</th>
                    </tr>
                </thead>
                <tbody style="color: var(--text-muted);">
                    <tr style="border-bottom: 1px solid var(--border);">
                        <td style="padding: 0.75rem;">Finder patterns</td>
                        <td style="padding: 0.75rem;">3 corners</td>
                        <td style="padding: 0.75rem;">1 corner</td>
                    </tr>
                    <tr style="border-bottom: 1px solid var(--border);">
                        <td style="padding: 0.75rem;">Max capacity</td>
                        <td style="padding: 0.75rem;">~3KB</td>
                        <td style="padding: 0.75rem;">~35 characters</td>
                    </tr>
                    <tr>
                        <td style="padding: 0.75rem;">Best for</td>
                        <td style="padding: 0.75rem;">General use</td>
                        <td style="padding: 0.75rem;">Small labels, PCBs</td>
                    </tr>
                </tbody>
            </table>
        </section>
    </div>
</div>
