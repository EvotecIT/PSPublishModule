---
title: Benchmarks - CodeGlyphX
description: Performance benchmarks comparing CodeGlyphX with other .NET barcode libraries. Transparent methodology and raw data.
slug: benchmarks
collection: pages
layout: home
meta.raw_html: true
meta.social: true
meta.social_description: Performance benchmarks comparing CodeGlyphX with other .NET barcode libraries.
meta.structured_data: true
meta.extra_scripts_file: benchmarks.scripts.html
canonical: https://codeglyphx.com/benchmarks/
---

<div class="benchmark-page">
    <section class="benchmark-hero">
        <h1>Performance Benchmarks</h1>
        <p class="benchmark-intro">
            Benchmarks measure specific scenarios under controlled, ideal lab conditions.
            Real-world performance varies based on input size, hardware, runtime, and many other factors.
            Treat these numbers as directional, not definitive.
        </p>
        <div class="benchmark-meta" data-benchmark-meta></div>
    </section>

    <section class="benchmark-section">
        <h2>About These Benchmarks</h2>

        <div class="benchmark-info-grid">
            <div class="benchmark-info-card">
                <h3>Quick vs Full</h3>
                <p>
                    <strong>Quick mode</strong> uses minimal iterations (warmup=1, iterations=3) for fast feedback during development.
                    Results have higher variance and should be treated as rough estimates.
                </p>
                <p>
                    <strong>Full mode</strong> uses BenchmarkDotNet defaults with statistical analysis, multiple iterations,
                    and outlier detection. These results are more reliable for comparisons.
                </p>
            </div>

            <div class="benchmark-info-card">
                <h3>What We Measure</h3>
                <ul>
                    <li><strong>Mean</strong> - Average time per operation (lower is better)</li>
                    <li><strong>Allocated</strong> - Managed memory per operation (lower is better)</li>
                </ul>
                <p>
                    Comparisons target PNG output and include both encoding and rendering time,
                    not just the encoding step.
                </p>
            </div>

            <div class="benchmark-info-card">
                <h3>Compared Libraries</h3>
                <ul>
                    <li><strong>ZXing.Net</strong> - with ImageSharp 3.x renderer</li>
                    <li><strong>QRCoder</strong> - PngByteQRCode (managed PNG)</li>
                    <li><strong>Barcoder</strong> - with ImageSharp renderer</li>
                </ul>
                <p>
                    Each library has different strengths. Configuration attempts to match
                    CodeGlyphX defaults where possible.
                </p>
            </div>

            <div class="benchmark-info-card">
                <h3>Limitations</h3>
                <ul>
                    <li>Benchmarks run on a single machine configuration</li>
                    <li>Numbers reflect ideal conditions, not production guarantees</li>
                    <li>Results may differ on other hardware/OS</li>
                    <li>Specific scenarios may not reflect your use case</li>
                    <li>Library versions and configurations affect results</li>
                </ul>
            </div>
        </div>
    </section>

    <section class="benchmark-section">
        <h2>Run Mode</h2>
        <div class="benchmark-mode-selector">
            <button class="benchmark-mode-btn active" data-mode="quick">Quick</button>
            <button class="benchmark-mode-btn" data-mode="full">Full</button>
        </div>
        <p class="benchmark-mode-note" data-mode-note></p>
    </section>

    <section class="benchmark-section">
        <h2>Comparison Summary</h2>
        <p class="section-description">
            How CodeGlyphX compares to the fastest library in each scenario.
            "vs Fastest" shows lag when CodeGlyphX is not fastest; when CodeGlyphX is fastest it also includes the lead vs the runner-up.
            Use the vendor columns and Δ lines for full context.
            See "Detailed Results" below for full vendor breakdowns.
        </p>
        <div class="benchmark-table-container" data-benchmark-summary>
            <p class="loading-text">Loading benchmark data...</p>
        </div>
    </section>

    <section class="benchmark-section">
        <h2>Performance Comparison</h2>
        <p class="section-description">
            Visual comparison of CodeGlyphX vs the fastest competitor in each scenario.
            Shorter bars are better (lower execution time).
        </p>
        <div class="benchmark-charts" data-benchmark-charts>
            <p class="loading-text">Loading chart data...</p>
        </div>
    </section>

    <section class="benchmark-section">
        <h2>Detailed Results</h2>
        <p class="section-description">
            Per-scenario breakdown showing all vendors tested. Not all scenarios have comparison data
            (some libraries don't support certain barcode types).
        </p>
        <div class="benchmark-details" data-benchmark-details>
            <p class="loading-text">Loading detailed results...</p>
        </div>
    </section>

    <section class="benchmark-section">
        <h2>Baseline Performance</h2>
        <p class="section-description">
            CodeGlyphX-only benchmarks without comparisons. Useful for tracking performance across versions.
        </p>
        <div class="benchmark-baseline" data-benchmark-baseline>
            <p class="loading-text">Loading baseline data...</p>
        </div>
    </section>

    <section class="benchmark-section">
        <h2>Environment</h2>
        <div class="benchmark-environment" data-benchmark-environment>
            <p class="loading-text">Loading environment info...</p>
        </div>
    </section>

    <section class="benchmark-section benchmark-notes">
        <h2>Methodology Notes</h2>
        <div data-benchmark-notes>
            <ul>
                <li>Comparisons target PNG output and include encode+render (not encode-only).</li>
                <li>Module size and quiet zone are matched to CodeGlyphX defaults where possible.</li>
                <li>Image dimensions are derived from CodeGlyphX module calculations.</li>
                <li>QR decode comparisons use raw RGBA32 bytes.</li>
                <li>QR decode "clean" uses balanced settings; "noisy" uses robust with aggressive sampling.</li>
            </ul>
        </div>
    </section>

    <section class="benchmark-section">
        <h2>Run Your Own</h2>
        <p>
            Clone the repository and run benchmarks on your own hardware:
        </p>
        <pre class="code-block"><code><span class="comment"># Quick benchmark (fast, less accurate)</span>
pwsh Build/Run-Benchmarks-Compare.ps1

<span class="comment"># Full benchmark (slow, statistically rigorous)</span>
pwsh Build/Run-Benchmarks-Compare.ps1 -Full

<span class="comment"># Generate report</span>
pwsh Build/Generate-BenchmarkReport.ps1 -ArtifactsPath Build/BenchmarkResults/&lt;run-folder&gt;</code></pre>
    </section>
</div>
