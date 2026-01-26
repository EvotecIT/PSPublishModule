---
title: FAQ - CodeGlyphX
description: Common questions about CodeGlyphX and QR/barcode generation in .NET.
slug: faq
collection: pages
layout: faq
meta.raw_html: true
meta.social: true
meta.social_description: Common questions about CodeGlyphX and QR/barcode generation in .NET.
meta.structured_data: true
meta.head_file: faq.head.html
meta.extra_scripts_file: faq.scripts.html
meta.breadcrumb_title: FAQ
canonical: https://codeglyphx.com/faq/
---

<div class="faq-page">
    <div class="faq-hero">
        <span class="section-label">Support</span>
        <h1>Frequently Asked Questions</h1>
        <p>Common questions about CodeGlyphX and QR/barcode generation in .NET.</p>
    </div>

    <div class="faq-content">
        <div class="faq-section">
            <h2>General</h2>
            <div class="faq-item" id="what-is-codeglyphx">
                <h3>What is CodeGlyphX?</h3>
                <p>CodeGlyphX is a zero-dependency .NET library for generating and decoding QR codes, barcodes, and 2D matrix codes. It's written entirely in C# with no native dependencies, making it fully portable across Windows, Linux, macOS, and WebAssembly.</p>
            </div>
            <div class="faq-item" id="which-dotnet-versions">
                <h3>Which .NET versions are supported?</h3>
                <p>CodeGlyphX targets multiple frameworks for maximum compatibility:</p><ul><li><strong>.NET 8.0 and .NET 10.0</strong> - Full support with AOT and trimming</li><li><strong>.NET Framework 4.7.2</strong> - For legacy Windows applications</li><li><strong>.NET Standard 2.0</strong> - For cross-platform compatibility with older runtimes</li></ul>
            </div>
            <div class="faq-item" id="is-it-free">
                <h3>Is CodeGlyphX free to use?</h3>
                <p>Yes. CodeGlyphX is open source and licensed under the <strong>Apache License 2.0</strong>. You can use it freely in both commercial and non-commercial projects.</p>
            </div>
            <div class="faq-item" id="zero-dependencies">
                <h3>What does "zero dependencies" mean?</h3>
                <p>On .NET 8.0+, CodeGlyphX has no external NuGet package dependencies beyond the runtime itself. It includes its own PNG encoder, image processing, and all encoding algorithms. This means smaller deployment size, no version conflicts, and no transitive dependency chains.</p><p><em>Note: .NET Standard 2.0 and .NET Framework 4.7.2 targets include System.Memory for Span&lt;T&gt; support.</em></p>
            </div>
        </div>
        <div class="faq-section">
            <h2>AOT & Trimming</h2>
            <div class="faq-item" id="aot-compatible">
                <h3>Does CodeGlyphX work with Native AOT?</h3>
                <p>Yes. On .NET 8.0 and .NET 10.0, CodeGlyphX is fully compatible with Native AOT compilation. It uses no reflection, no dynamic code generation, and no runtime IL emission. You can publish AOT-compiled applications that use CodeGlyphX without any warnings or runtime issues.</p>
            </div>
            <div class="faq-item" id="trimming-safe">
                <h3>Is it trimming-safe?</h3>
                <p>Yes. On .NET 8.0+, CodeGlyphX is annotated for full trimming compatibility. When you publish with <code>PublishTrimmed=true</code>, the linker can safely remove unused code paths without breaking functionality. No <code>TrimmerRootAssembly</code> workarounds needed.</p>
            </div>
            <div class="faq-item" id="single-file">
                <h3>Can I use it in single-file deployments?</h3>
                <p>Yes. On .NET 8.0+, CodeGlyphX works perfectly with <code>PublishSingleFile=true</code>. Since it has no native dependencies or satellite assemblies, your entire application including QR generation can be a single executable file.</p>
            </div>
        </div>
        <div class="faq-section">
            <h2>Features & Capabilities</h2>
            <div class="faq-item" id="supported-formats">
                <h3>What code formats are supported?</h3>
                <p>CodeGlyphX supports a wide range of 1D and 2D codes:</p><ul><li><strong>QR Codes:</strong> All versions (1-40), all error correction levels (L/M/Q/H)</li><li><strong>1D Barcodes:</strong> Code 128, GS1-128, Code 39, Code 93, Code 11, Codabar, MSI, Plessey, Telepen, EAN-13, EAN-8, UPC-A, UPC-E, ITF-14, ITF</li><li><strong>2D Matrix:</strong> Data Matrix, PDF417, Aztec Code</li></ul>
            </div>
            <div class="faq-item" id="special-payloads">
                <h3>Can I generate WiFi, vCard, or OTP QR codes?</h3>
                <p>Yes. CodeGlyphX includes built-in payload generators for common QR code use cases: WiFi network credentials, vCard contacts, email links, phone numbers, SMS messages, OTP/TOTP authenticator setup, SEPA/Girocode payments, and more.</p>
            </div>
            <div class="faq-item" id="customization">
                <h3>Can I customize colors and shapes?</h3>
                <p>Yes. QR codes can be customized with custom foreground/background colors, different module shapes (square, circle, rounded), custom eye styles, and embedded logos. Barcodes support custom colors and sizing options.</p>
            </div>
            <div class="faq-item" id="decoding">
                <h3>Can CodeGlyphX decode/read barcodes?</h3>
                <p>Yes. CodeGlyphX includes a barcode decoder that can read QR codes and 1D barcodes from images. It supports multiple image formats (PNG, JPEG, BMP, GIF, TIFF) and can detect multiple codes in a single image.</p>
            </div>
            <div class="faq-item" id="output-formats">
                <h3>What output formats are supported?</h3>
                <p>CodeGlyphX can output to PNG (with its built-in encoder), SVG (vector graphics), raw pixel data (for custom rendering), ASCII art (for terminal output), HTML, PDF, EPS, and more. The PNG encoder requires no external libraries.</p>
            </div>
        </div>
        <div class="faq-section">
            <h2>Platform Support</h2>
            <div class="faq-item" id="blazor-webassembly">
                <h3>Does it work with Blazor WebAssembly?</h3>
                <p>Yes. CodeGlyphX runs entirely in the browser via WebAssembly. The playground on this website demonstrates this - it generates codes in real-time using the actual CodeGlyphX library compiled to WASM. No server round-trips needed.</p>
            </div>
            <div class="faq-item" id="maui">
                <h3>Does it work with .NET MAUI?</h3>
                <p>Yes. CodeGlyphX works on all MAUI platforms: Android, iOS, macOS, and Windows. Since it has no platform-specific dependencies, the same code works identically across all targets.</p>
            </div>
            <div class="faq-item" id="aspnet">
                <h3>Can I use it in ASP.NET Core?</h3>
                <p>Yes. CodeGlyphX is commonly used in ASP.NET Core applications to generate QR codes dynamically. You can return PNG images directly from controllers or minimal API endpoints, generate codes in Razor Pages, or use it in background services.</p>
            </div>
            <div class="faq-item" id="azure-functions">
                <h3>Does it work in Azure Functions / AWS Lambda?</h3>
                <p>Yes. CodeGlyphX's zero-dependency design makes it ideal for serverless environments. No native libraries to configure, no special deployment steps. It works out of the box in both consumption and premium plans.</p>
            </div>
        </div>
        <div class="faq-section">
            <h2>Performance</h2>
            <div class="faq-item" id="performance">
                <h3>How fast is CodeGlyphX?</h3>
                <p>CodeGlyphX is optimized for performance. QR code and barcode generation is fast enough for real-time use cases including API endpoints and interactive applications. The library is suitable for high-throughput scenarios like batch generation.</p>
            </div>
            <div class="faq-item" id="memory">
                <h3>What about memory usage?</h3>
                <p>On .NET 8.0+, CodeGlyphX uses <code>Span&lt;T&gt;</code> and <code>Memory&lt;T&gt;</code> to minimize allocations where possible. The library is designed to be efficient for both single-use and batch processing scenarios.</p>
            </div>
        </div>
        <div class="faq-section">
            <h2>Troubleshooting</h2>
            <div class="faq-item" id="qr-not-scanning">
                <h3>My QR code won't scan. What's wrong?</h3>
                <p>Common reasons QR codes fail to scan:</p><ul><li><strong>Low contrast:</strong> Ensure sufficient contrast between foreground and background colors</li><li><strong>Too small:</strong> QR codes need adequate size relative to scanning distance</li><li><strong>Error correction too low:</strong> Use higher error correction (Q or H) for printed/damaged codes</li><li><strong>Quiet zone missing:</strong> Leave white space around the code (CodeGlyphX adds this automatically)</li></ul>
            </div>
            <div class="faq-item" id="barcode-invalid">
                <h3>I'm getting "invalid data" errors for barcodes</h3>
                <p>Each barcode type has specific character set and length requirements. For example, EAN-13 must be exactly 12 or 13 digits, UPC-A must be 11 or 12 digits, and Code 39 only supports uppercase letters and certain symbols. Check the documentation for the specific requirements of your barcode type.</p>
            </div>
            <div class="faq-item" id="help">
                <h3>Where can I get help?</h3>
                <p>For questions, bug reports, or feature requests, please open an issue on the <a href="https://github.com/EvotecIT/CodeGlyphX/issues" target="_blank" rel="noopener">GitHub repository</a>. For documentation and examples, see the <a href="/docs/">Documentation</a> section.</p>
            </div>
        </div>
    </div>
</div>
