---
title: CodeGlyphX - Zero-Dependency QR & Barcode Toolkit for .NET
description: CodeGlyphX is a blazing-fast, zero-dependency .NET library for generating and decoding QR codes, barcodes, Data Matrix, PDF417, and Aztec codes.
slug: index
collection: pages
layout: home
meta.raw_html: true
meta.extra_scripts_file: index.scripts.html
---

<!-- Hero Section -->
<div class="hero">
    <div class="hero-content">
        <div class="hero-badge">
            <span class="hero-badge-dot"></span>
            <span>Open Source &bull; Apache 2.0 License</span>
        </div>

        <h1>Generate QR Codes &amp; Barcodes<br/>Without Dependencies</h1>

        <p class="hero-tagline">
            CodeGlyphX is a blazing-fast, zero-dependency .NET library for encoding and decoding
            QR codes, Data Matrix, PDF417, Aztec, and all major 1D barcode formats.
            No System.Drawing. No SkiaSharp. Just pure .NET.
        </p>

        <div class="hero-buttons">
            <a href="/playground/" class="btn btn-primary">
                <svg class="btn-icon" viewBox="0 0 24 24" fill="currentColor">
                    <path d="M8 5v14l11-7z"/>
                </svg>
                Try the Playground
            </a>
            <a href="https://www.nuget.org/packages/CodeGlyphX" target="_blank" class="btn btn-secondary">
                <svg class="btn-icon" viewBox="0 0 24 24" fill="currentColor">
                    <path d="M12 2L2 7l10 5 10-5-10-5zM2 17l10 5 10-5M2 12l10 5 10-5"/>
                </svg>
                View on NuGet
            </a>
        </div>

        <div class="install-command">
            <code>dotnet add package CodeGlyphX</code>
            <button class="copy-btn" type="button" data-copy="dotnet add package CodeGlyphX" title="Copy install command">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <rect x="9" y="9" width="13" height="13" rx="2"/>
                    <path d="M5 15H4a2 2 0 01-2-2V4a2 2 0 012-2h9a2 2 0 012 2v1"/>
                </svg>
            </button>
        </div>

        <div class="hero-badges">
            <a href="https://github.com/EvotecIT/CodeGlyphX" target="_blank" rel="noopener" class="hero-badge-link">
                <img src="https://img.shields.io/github/stars/EvotecIT/CodeGlyphX?style=social" alt="GitHub stars" width="100" height="20" loading="lazy" />
            </a>
            <a href="https://www.nuget.org/packages/CodeGlyphX" target="_blank" rel="noopener" class="hero-badge-link">
                <img src="https://img.shields.io/nuget/dt/CodeGlyphX" alt="NuGet downloads" width="100" height="20" loading="lazy" />
            </a>
        </div>

        <div class="hero-code-preview">
            <div class="code-preview-item qr-preview" title="QR Code">
                <svg viewBox="0 0 21 21" fill="currentColor">
                    <!-- Top-left finder: 7x7 with hollow center -->
                    <path d="M0,0h7v7h-7zM1,1v5h5v-5zM2,2h3v3h-3z"/>
                    <!-- Top-right finder -->
                    <path d="M14,0h7v7h-7zM15,1v5h5v-5zM16,2h3v3h-3z"/>
                    <!-- Bottom-left finder -->
                    <path d="M0,14h7v7h-7zM1,15v5h5v-5zM2,16h3v3h-3z"/>
                    <!-- Timing patterns -->
                    <rect x="8" y="6" width="1" height="1"/><rect x="10" y="6" width="1" height="1"/><rect x="12" y="6" width="1" height="1"/>
                    <rect x="6" y="8" width="1" height="1"/><rect x="6" y="10" width="1" height="1"/><rect x="6" y="12" width="1" height="1"/>
                    <!-- Data area -->
                    <rect x="8" y="8" width="1" height="1"/><rect x="10" y="8" width="1" height="1"/><rect x="12" y="8" width="1" height="1"/>
                    <rect x="9" y="9" width="1" height="1"/><rect x="11" y="9" width="1" height="1"/>
                    <rect x="8" y="10" width="1" height="1"/><rect x="10" y="10" width="1" height="1"/><rect x="12" y="10" width="1" height="1"/>
                    <rect x="9" y="11" width="1" height="1"/><rect x="11" y="11" width="1" height="1"/>
                    <rect x="8" y="12" width="1" height="1"/><rect x="10" y="12" width="1" height="1"/><rect x="12" y="12" width="1" height="1"/>
                    <rect x="14" y="8" width="1" height="1"/><rect x="16" y="9" width="1" height="1"/><rect x="18" y="8" width="1" height="1"/><rect x="15" y="10" width="1" height="1"/><rect x="17" y="11" width="1" height="1"/><rect x="19" y="10" width="1" height="1"/>
                    <rect x="8" y="14" width="1" height="1"/><rect x="10" y="15" width="1" height="1"/><rect x="12" y="14" width="1" height="1"/><rect x="9" y="16" width="1" height="1"/><rect x="11" y="17" width="1" height="1"/>
                    <rect x="14" y="14" width="1" height="1"/><rect x="16" y="15" width="1" height="1"/><rect x="18" y="14" width="1" height="1"/><rect x="15" y="16" width="1" height="1"/><rect x="17" y="17" width="1" height="1"/><rect x="19" y="16" width="1" height="1"/><rect x="20" y="18" width="1" height="1"/>
                </svg>
            </div>
            <div class="code-preview-item barcode-preview" title="Code 128">
                <svg viewBox="0 0 67 40" fill="currentColor">
                    <rect x="0" y="0" width="2" height="40"/><rect x="3" y="0" width="1" height="40"/><rect x="5" y="0" width="2" height="40"/><rect x="9" y="0" width="1" height="40"/><rect x="11" y="0" width="3" height="40"/><rect x="15" y="0" width="1" height="40"/><rect x="18" y="0" width="1" height="40"/><rect x="20" y="0" width="2" height="40"/><rect x="24" y="0" width="1" height="1"/><rect x="26" y="0" width="3" height="40"/><rect x="31" y="0" width="1" height="40"/><rect x="33" y="0" width="2" height="40"/><rect x="37" y="0" width="1" height="40"/><rect x="39" y="0" width="1" height="40"/><rect x="42" y="0" width="2" height="40"/><rect x="45" y="0" width="1" height="40"/><rect x="48" y="0" width="3" height="40"/><rect x="52" y="0" width="1" height="40"/><rect x="55" y="0" width="2" height="40"/><rect x="58" y="0" width="1" height="40"/><rect x="61" y="0" width="1" height="40"/><rect x="63" y="0" width="2" height="40"/><rect x="66" y="0" width="1" height="40"/>
                </svg>
            </div>
            <div class="code-preview-item matrix-preview" title="Data Matrix">
                <svg viewBox="0 0 14 14" fill="currentColor">
                    <!-- L-finder: solid left column and top row -->
                    <rect x="0" y="0" width="14" height="1"/><rect x="0" y="1" width="1" height="13"/>
                    <!-- Clock track: alternating on right and bottom -->
                    <rect x="13" y="1" width="1" height="1"/><rect x="13" y="3" width="1" height="1"/><rect x="13" y="5" width="1" height="1"/><rect x="13" y="7" width="1" height="1"/><rect x="13" y="9" width="1" height="1"/><rect x="13" y="11" width="1" height="1"/><rect x="13" y="13" width="1" height="1"/>
                    <rect x="2" y="13" width="1" height="1"/><rect x="4" y="13" width="1" height="1"/><rect x="6" y="13" width="1" height="1"/><rect x="8" y="13" width="1" height="1"/><rect x="10" y="13" width="1" height="1"/><rect x="12" y="13" width="1" height="1"/>
                    <!-- Dense data fill -->
                    <rect x="2" y="2" width="1" height="1"/><rect x="3" y="2" width="1" height="1"/><rect x="5" y="2" width="1" height="1"/><rect x="7" y="2" width="1" height="1"/><rect x="8" y="2" width="1" height="1"/><rect x="10" y="2" width="1" height="1"/><rect x="11" y="2" width="1" height="1"/>
                    <rect x="2" y="3" width="1" height="1"/><rect x="4" y="3" width="1" height="1"/><rect x="6" y="3" width="1" height="1"/><rect x="8" y="3" width="1" height="1"/><rect x="9" y="3" width="1" height="1"/><rect x="11" y="3" width="1" height="1"/>
                    <rect x="3" y="4" width="1" height="1"/><rect x="4" y="4" width="1" height="1"/><rect x="6" y="4" width="1" height="1"/><rect x="7" y="4" width="1" height="1"/><rect x="9" y="4" width="1" height="1"/><rect x="10" y="4" width="1" height="1"/><rect x="12" y="4" width="1" height="1"/>
                    <rect x="2" y="5" width="1" height="1"/><rect x="5" y="5" width="1" height="1"/><rect x="6" y="5" width="1" height="1"/><rect x="8" y="5" width="1" height="1"/><rect x="10" y="5" width="1" height="1"/><rect x="11" y="5" width="1" height="1"/>
                    <rect x="3" y="6" width="1" height="1"/><rect x="4" y="6" width="1" height="1"/><rect x="7" y="6" width="1" height="1"/><rect x="8" y="6" width="1" height="1"/><rect x="9" y="6" width="1" height="1"/><rect x="11" y="6" width="1" height="1"/><rect x="12" y="6" width="1" height="1"/>
                    <rect x="2" y="7" width="1" height="1"/><rect x="4" y="7" width="1" height="1"/><rect x="5" y="7" width="1" height="1"/><rect x="7" y="7" width="1" height="1"/><rect x="9" y="7" width="1" height="1"/><rect x="10" y="7" width="1" height="1"/>
                    <rect x="3" y="8" width="1" height="1"/><rect x="5" y="8" width="1" height="1"/><rect x="6" y="8" width="1" height="1"/><rect x="8" y="8" width="1" height="1"/><rect x="10" y="8" width="1" height="1"/><rect x="11" y="8" width="1" height="1"/><rect x="12" y="8" width="1" height="1"/>
                    <rect x="2" y="9" width="1" height="1"/><rect x="4" y="9" width="1" height="1"/><rect x="6" y="9" width="1" height="1"/><rect x="7" y="9" width="1" height="1"/><rect x="9" y="9" width="1" height="1"/><rect x="11" y="9" width="1" height="1"/>
                    <rect x="3" y="10" width="1" height="1"/><rect x="4" y="10" width="1" height="1"/><rect x="5" y="10" width="1" height="1"/><rect x="7" y="10" width="1" height="1"/><rect x="8" y="10" width="1" height="1"/><rect x="10" y="10" width="1" height="1"/><rect x="12" y="10" width="1" height="1"/>
                    <rect x="2" y="11" width="1" height="1"/><rect x="5" y="11" width="1" height="1"/><rect x="6" y="11" width="1" height="1"/><rect x="8" y="11" width="1" height="1"/><rect x="9" y="11" width="1" height="1"/><rect x="11" y="11" width="1" height="1"/>
                    <rect x="3" y="12" width="1" height="1"/><rect x="4" y="12" width="1" height="1"/><rect x="6" y="12" width="1" height="1"/><rect x="7" y="12" width="1" height="1"/><rect x="9" y="12" width="1" height="1"/><rect x="10" y="12" width="1" height="1"/><rect x="11" y="12" width="1" height="1"/>
                </svg>
            </div>
        </div>
    </div>
</div>

<!-- Stats Section -->
<section class="stats">
    <div class="stats-grid">
        <div class="stat-item">
            <span class="stat-number">18+</span>
            <p>Barcode Formats</p>
        </div>
        <div class="stat-item">
            <span class="stat-number">15+</span>
            <p>Output Formats</p>
        </div>
        <div class="stat-item">
            <span class="stat-number">0</span>
            <p>Dependencies</p>
        </div>
        <div class="stat-item">
            <span class="stat-number">3</span>
            <p>Platforms</p>
        </div>
    </div>
</section>

<!-- Features Section -->
<section class="features">
    <div class="section-header">
        <span class="section-label">Why CodeGlyphX?</span>
        <h2>Built for Modern .NET Development</h2>
        <p>Everything you need for barcode generation and scanning, with none of the bloat.</p>
    </div>

    <div class="features-grid">
        <div class="feature-card">
            <div class="feature-icon">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                    <path d="M13 10V3L4 14h7v7l9-11h-7z"/>
                </svg>
            </div>
            <h3>Zero Dependencies</h3>
            <p>No System.Drawing, SkiaSharp, or ImageSharp required. Pure managed code that deploys anywhere without native library headaches.</p>
        </div>

        <div class="feature-card">
            <div class="feature-icon">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                    <path d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15"/>
                </svg>
            </div>
            <h3>Encode &amp; Decode</h3>
            <p>Full round-trip support. Generate codes and read them back from PNG, JPEG, BMP, GIF, and more with robust pixel detection.</p>
        </div>

        <div class="feature-card">
            <div class="feature-icon">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                    <rect x="3" y="3" width="7" height="7" rx="1"/>
                    <rect x="14" y="3" width="7" height="7" rx="1"/>
                    <rect x="3" y="14" width="7" height="7" rx="1"/>
                    <rect x="14" y="14" width="7" height="7" rx="1"/>
                </svg>
            </div>
            <h3>2D Codes</h3>
            <p>QR Code, Micro QR, Data Matrix, PDF417, and Aztec with full ECI, Kanji, structured append, and FNC1/GS1 support.</p>
        </div>

        <div class="feature-card">
            <div class="feature-icon">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                    <rect x="2" y="6" width="2" height="12"/>
                    <rect x="5" y="6" width="1" height="12"/>
                    <rect x="7" y="6" width="3" height="12"/>
                    <rect x="11" y="6" width="1" height="12"/>
                    <rect x="13" y="6" width="2" height="12"/>
                    <rect x="16" y="6" width="1" height="12"/>
                    <rect x="18" y="6" width="2" height="12"/>
                    <rect x="21" y="6" width="1" height="12"/>
                </svg>
            </div>
            <h3>1D Barcodes</h3>
            <p>Code 128, GS1-128, Code 39, Code 93, Codabar, MSI, Plessey, EAN-8/13, UPC-A/E, ITF-14, and more.</p>
        </div>

        <div class="feature-card">
            <div class="feature-icon">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                    <path d="M4 16l4.586-4.586a2 2 0 012.828 0L16 16m-2-2l1.586-1.586a2 2 0 012.828 0L20 14m-6-6h.01M6 20h12a2 2 0 002-2V6a2 2 0 00-2-2H6a2 2 0 00-2 2v12a2 2 0 002 2z"/>
                </svg>
            </div>
            <h3>Multiple Outputs</h3>
            <p>Render to PNG, JPEG, BMP, SVG, SVGZ, PDF, EPS, HTML, ASCII art, and exotic formats like PPM, PBM, TGA, ICO, XBM, XPM.</p>
        </div>

        <div class="feature-card">
            <div class="feature-icon">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                    <path d="M9.663 17h4.673M12 3v1m6.364 1.636l-.707.707M21 12h-1M4 12H3m3.343-5.657l-.707-.707m2.828 9.9a5 5 0 117.072 0l-.548.547A3.374 3.374 0 0014 18.469V19a2 2 0 11-4 0v-.531c0-.895-.356-1.754-.988-2.386l-.548-.547z"/>
                </svg>
            </div>
            <h3>AOT &amp; Trim Ready</h3>
            <p>No reflection, no runtime codegen. Fully compatible with Native AOT publishing and aggressive trimming.</p>
        </div>

        <div class="feature-card">
            <div class="feature-icon">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                    <path d="M21 12a9 9 0 01-9 9m9-9a9 9 0 00-9-9m9 9H3m9 9a9 9 0 01-9-9m9 9c1.657 0 3-4.03 3-9s-1.343-9-3-9m0 18c-1.657 0-3-4.03-3-9s1.343-9 3-9m-9 9a9 9 0 019-9"/>
                </svg>
            </div>
            <h3>Cross-Platform</h3>
            <p>Runs identically on Windows, Linux, and macOS. Targets .NET 8+, .NET Standard 2.0, and .NET Framework 4.7.2.</p>
        </div>

        <div class="feature-card">
            <div class="feature-icon">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                    <path d="M3 10h18M7 15h1m4 0h1m-7 4h12a3 3 0 003-3V8a3 3 0 00-3-3H6a3 3 0 00-3 3v8a3 3 0 003 3z"/>
                </svg>
            </div>
            <h3>Payment Helpers</h3>
            <p>Built-in support for SEPA Girocode, Swiss QR Bill, BezahlCode, UPI, and cryptocurrency addresses.</p>
        </div>

        <div class="feature-card">
            <div class="feature-icon">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                    <path d="M12 15v2m-6 4h12a2 2 0 002-2v-6a2 2 0 00-2-2H6a2 2 0 00-2 2v6a2 2 0 002 2zm10-10V7a4 4 0 00-8 0v4h8z"/>
                </svg>
            </div>
            <h3>OTP Support</h3>
            <p>Generate otpauth:// TOTP and HOTP codes compatible with Google Authenticator, Microsoft Authenticator, and Authy.</p>
        </div>
    </div>
</section>

<!-- QR Style Board -->
<section class="style-board">
    <div class="section-header">
        <span class="section-label">QR Styling</span>
        <h2>Style Boards Built with CodeGlyphX</h2>
        <p>Curated styles generated by the library itself. Crisp, scalable, and ready for big screens.</p>
    </div>

    <div class="style-board-grid"
         data-style-board
         data-style-board-src="/data/style-board.json"
         data-style-board-base="/assets/style-board/">
        <div class="style-board-fallback">Loading style board…</div>
    </div>

    <div class="style-board-actions">
        <a href="/playground/" class="btn btn-secondary">Open Playground</a>
        <a href="/docs/" class="btn btn-secondary">Read Styling Docs</a>
    </div>
</section>

<!-- Symbologies Section -->
<section class="symbologies">
    <div class="section-header">
        <span class="section-label">Supported Formats</span>
        <h2>Every Barcode You Need</h2>
        <p>From retail EAN codes to industrial Data Matrix, CodeGlyphX has you covered.</p>
    </div>

    <div class="symbology-category">
        <h3>2D Matrix Codes</h3>
        <div class="symbology-grid">
            <span class="symbology-tag">QR Code</span>
            <span class="symbology-tag">Micro QR</span>
            <span class="symbology-tag">Data Matrix</span>
            <span class="symbology-tag">PDF417</span>
            <span class="symbology-tag">Aztec</span>
        </div>
    </div>

    <div class="symbology-category">
        <h3>1D Linear Barcodes</h3>
        <div class="symbology-grid">
            <span class="symbology-tag">Code 128</span>
            <span class="symbology-tag">GS1-128</span>
            <span class="symbology-tag">Code 39</span>
            <span class="symbology-tag">Code 93</span>
            <span class="symbology-tag">Code 11</span>
            <span class="symbology-tag">Codabar</span>
            <span class="symbology-tag">MSI</span>
            <span class="symbology-tag">Plessey</span>
            <span class="symbology-tag">EAN-8</span>
            <span class="symbology-tag">EAN-13</span>
            <span class="symbology-tag">UPC-A</span>
            <span class="symbology-tag">UPC-E</span>
            <span class="symbology-tag">ITF-14</span>
        </div>
    </div>

    <div class="symbology-category">
        <h3>Payload Types</h3>
        <div class="symbology-grid">
            <span class="symbology-tag">URL / Text</span>
            <span class="symbology-tag">WiFi Config</span>
            <span class="symbology-tag">vCard Contact</span>
            <span class="symbology-tag">MeCard</span>
            <span class="symbology-tag">Calendar Event</span>
            <span class="symbology-tag">Email / Phone / SMS</span>
            <span class="symbology-tag">TOTP / HOTP</span>
            <span class="symbology-tag">SEPA Girocode</span>
            <span class="symbology-tag">Swiss QR Bill</span>
            <span class="symbology-tag">UPI Payment</span>
            <span class="symbology-tag">Bitcoin / Crypto</span>
            <span class="symbology-tag">App Store Links</span>
        </div>
    </div>
</section>

<!-- Code Examples Section -->
<section class="code-examples">
    <div class="section-header">
        <span class="section-label">Simple API</span>
        <h2>One Line of Code</h2>
        <p>Generate any barcode format with intuitive, discoverable APIs.</p>
    </div>

    <div class="code-example-container">
        <pre class="code-block"><span class="keyword">using</span> CodeGlyphX;

<span class="comment">// QR Code - one liner</span>
QR.Save(<span class="string">"https://evotec.xyz"</span>, <span class="string">"website.png"</span>);
QR.Save(<span class="string">"https://evotec.xyz"</span>, <span class="string">"website.svg"</span>);
QR.Save(<span class="string">"https://evotec.xyz"</span>, <span class="string">"website.pdf"</span>);

<span class="comment">// Barcodes</span>
Barcode.Save(BarcodeType.Code128, <span class="string">"PRODUCT-12345"</span>, <span class="string">"barcode.png"</span>);
Barcode.Save(BarcodeType.Ean13, <span class="string">"5901234123457"</span>, <span class="string">"ean.png"</span>);

<span class="comment">// 2D codes</span>
DataMatrixCode.Save(<span class="string">"Serial: ABC123"</span>, <span class="string">"datamatrix.png"</span>);
Pdf417Code.Save(<span class="string">"Document ID: 98765"</span>, <span class="string">"pdf417.png"</span>);
AztecCode.Save(<span class="string">"Ticket: CONF-2024"</span>, <span class="string">"aztec.png"</span>);

<span class="comment">// Decode from image</span>
<span class="keyword">if</span> (QrImageDecoder.TryDecodeImage(File.ReadAllBytes(<span class="string">"qr.png"</span>), <span class="keyword">out var</span> result))
{
    Console.WriteLine(result.Text);
}</pre>
    </div>
</section>

<!-- Payload Examples -->
<section class="code-examples" style="background: var(--bg-card);">
    <div class="section-header">
        <span class="section-label">Rich Payloads</span>
        <h2>More Than Just Text</h2>
        <p>Built-in helpers for WiFi, contacts, payments, authentication, and more.</p>
    </div>

    <div class="code-example-container">
        <pre class="code-block"><span class="keyword">using</span> CodeGlyphX;
<span class="keyword">using</span> CodeGlyphX.Payloads;

<span class="comment">// WiFi configuration</span>
QR.Save(QrPayloads.Wifi(<span class="string">"MyNetwork"</span>, <span class="string">"SecurePassword123"</span>), <span class="string">"wifi.png"</span>);

<span class="comment">// 2FA / OTP codes (Google Authenticator, Authy, etc.)</span>
QR.Save(QrPayloads.OneTimePassword(
    OtpAuthType.Totp,
    <span class="string">"JBSWY3DPEHPK3PXP"</span>,
    label: <span class="string">"user@example.com"</span>,
    issuer: <span class="string">"MyApp"</span>
), <span class="string">"otp.png"</span>);

<span class="comment">// Contact card</span>
QR.Save(QrPayloads.VCard(
    firstName: <span class="string">"Przemyslaw"</span>,
    lastName: <span class="string">"Klys"</span>,
    email: <span class="string">"contact@evotec.pl"</span>,
    website: <span class="string">"https://evotec.xyz"</span>
), <span class="string">"contact.png"</span>);

<span class="comment">// SEPA payment (European bank transfer)</span>
QR.Save(QrPayloads.Girocode(
    iban: <span class="string">"DE89370400440532013000"</span>,
    bic: <span class="string">"COBADEFFXXX"</span>,
    recipientName: <span class="string">"Evotec Services"</span>,
    amount: 99.99m,
    reference: <span class="string">"Invoice 2024-001"</span>
), <span class="string">"payment.png"</span>);</pre>
    </div>
</section>

<!-- CTA Section -->
<section class="cta">
    <div class="cta-content">
        <h2>Ready to Get Started?</h2>
        <p>
            Install CodeGlyphX via NuGet and start generating barcodes in minutes.
            No configuration, no native dependencies, just pure .NET.
        </p>
        <div class="hero-buttons" style="margin-bottom: 0;">
            <a href="https://www.nuget.org/packages/CodeGlyphX" target="_blank" class="btn btn-primary">
                Install from NuGet
            </a>
            <a href="https://github.com/EvotecIT/CodeGlyphX" target="_blank" class="btn btn-secondary">
                View Source on GitHub
            </a>
        </div>
    </div>
</section>

<!-- About Section -->
<section class="about-section">
    <div class="about-content">
        <p class="about-text">
            <strong>CodeGlyphX</strong> is developed and maintained by
            <a href="https://twitter.com/PrzemyslawKlys" target="_blank" rel="noopener">Przemyslaw Klys</a>
            at <a href="https://evotec.xyz" target="_blank" rel="noopener">Evotec Services sp. z o.o.</a>
            <br/><br/>
            We build open-source tools for the .NET ecosystem, including PowerShell modules,
            libraries, and developer utilities. Check out our other projects on
            <a href="https://github.com/EvotecIT" target="_blank" rel="noopener">our GitHub organization</a>.
        </p>
    </div>
</section>
