---
title: Showcase - CodeGlyphX
description: See CodeGlyphX in action powering real-world applications.
slug: showcase
collection: pages
layout: showcase
meta.raw_html: true
meta.social: true
meta.social_description: See CodeGlyphX in action powering real-world applications.
meta.structured_data: true
meta.extra_scripts_file: showcase.scripts.html
canonical: https://codeglyphx.com/showcase/
---

<div class="showcase-page">
    <div class="showcase-hero">
        <span class="section-label">Built with CodeGlyphX</span>
        <h1>Showcase</h1>
        <p>See CodeGlyphX in action powering real-world applications.</p>
    </div>

    <div class="showcase-grid">
        <!-- Information Box Card -->
        <div class="showcase-card showcase-card-large">
            <div class="showcase-header">
                <div class="showcase-icon"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5"><path d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z"/></svg></div>
                <div class="showcase-title">
                    <h2>Information Box</h2>
                    <span class="showcase-badge">Windows Desktop</span>
                </div>
            </div>
            <div class="showcase-meta">
                <span class="showcase-license"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M9 12l2 2 4-4m5.618-4.016A11.955 11.955 0 0112 2.944a11.955 11.955 0 01-8.618 3.04A12.02 12.02 0 003 9c0 5.591 3.824 10.29 9 11.622 5.176-1.332 9-6.03 9-11.622 0-1.042-.133-2.052-.382-3.016z"/></svg>AGPL-3.0</span>
                <span class="showcase-tech">.NET 8</span>
                <span class="showcase-tech">WPF</span>
                <span class="showcase-tech">MVVM</span>
            </div>
            <p class="showcase-description">Modern, secret-free IT self-service desktop app for Windows. Shows device/account/network status, warns about password expiry, exposes tenant-aware quick links, and ships with built-in "Fix" actions for common end-user issues. Uses CodeGlyphX for OTP QR code generation in the authentication tab.</p>
            <div class="showcase-details">
                <h4>Key Features</h4>
                <ul>
                    <li>Cross-tenant, secret-free: works with Microsoft Graph when available, degrades gracefully offline/LDAP</li>
                    <li>Built-in OTP tab with QR code generation via CodeGlyphX for TOTP/HOTP setup</li>
                    <li>"Fix" tab with typed, AOT-friendly built-ins (OneDrive/Teams/VPN/Store/logs)</li>
                    <li>Themeable (Auto/Light/Dark/Classic/Ocean/Forest/Sunset) with white-label branding</li>
                    <li>Portable deployment: single-contained, single-fx, portable outputs from one script</li>
                </ul>
            </div>
            <div class="showcase-features">
                <span class="showcase-feature"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" width="16" height="16"><path d="M5 13l4 4L19 7"/></svg>OTP QR Generation</span>
                <span class="showcase-feature"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" width="16" height="16"><path d="M5 13l4 4L19 7"/></svg>Multi-Tenant</span>
                <span class="showcase-feature"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" width="16" height="16"><path d="M5 13l4 4L19 7"/></svg>7 Themes</span>
                <span class="showcase-feature"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" width="16" height="16"><path d="M5 13l4 4L19 7"/></svg>Portable EXE</span>
            </div>
            <div class="showcase-gallery" data-carousel="carousel-informationbox">
                <div class="showcase-gallery-tabs">
                    <button class="showcase-gallery-tab active" data-theme="dark">Dark Theme</button>
                    <button class="showcase-gallery-tab" data-theme="light">Light Theme</button>
                </div>
                <div class="showcase-carousel">
                    <div class="showcase-carousel-viewport">
                        <div class="showcase-carousel-slide active" data-theme="dark" data-index="0">
                            <img src="/images/showcase/informationbox-dark-status.png" alt="Information Box dark theme - Status tab showing device and network information" loading="lazy" />
                        </div>
                        <div class="showcase-carousel-slide" data-theme="dark" data-index="1">
                            <img src="/images/showcase/informationbox-dark-account.png" alt="Information Box dark theme - Account tab with user details from Active Directory" loading="lazy" />
                        </div>
                        <div class="showcase-carousel-slide" data-theme="dark" data-index="2">
                            <img src="/images/showcase/informationbox-dark-otp.png" alt="Information Box dark theme - OTP tab with QR code generated by CodeGlyphX" loading="lazy" />
                        </div>
                        <div class="showcase-carousel-slide" data-theme="dark" data-index="3">
                            <img src="/images/showcase/informationbox-dark-troubleshoot.png" alt="Information Box dark theme - Troubleshoot tab with built-in fix actions" loading="lazy" />
                        </div>
                        <div class="showcase-carousel-slide" data-theme="light" data-index="0" style="display:none">
                            <img src="/images/showcase/informationbox-light-status.png" alt="Information Box light theme - Status tab showing device and network information" loading="lazy" />
                        </div>
                        <div class="showcase-carousel-slide" data-theme="light" data-index="1" style="display:none">
                            <img src="/images/showcase/informationbox-light-account.png" alt="Information Box light theme - Account tab with user details from Active Directory" loading="lazy" />
                        </div>
                        <div class="showcase-carousel-slide" data-theme="light" data-index="2" style="display:none">
                            <img src="/images/showcase/informationbox-light-otp.png" alt="Information Box light theme - OTP tab with QR code generated by CodeGlyphX" loading="lazy" />
                        </div>
                        <div class="showcase-carousel-slide" data-theme="light" data-index="3" style="display:none">
                            <img src="/images/showcase/informationbox-light-troubleshoot.png" alt="Information Box light theme - Troubleshoot tab with built-in fix actions" loading="lazy" />
                        </div>
                        <button class="showcase-carousel-nav prev" aria-label="Previous image"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M15 19l-7-7 7-7"/></svg></button>
                        <button class="showcase-carousel-nav next" aria-label="Next image"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M9 5l7 7-7 7"/></svg></button>
                    </div>
                    <div class="showcase-carousel-footer">
                        <span class="showcase-carousel-caption">Status Tab</span>
                        <div class="showcase-carousel-dots">
                            <button class="showcase-carousel-dot active" data-index="0" aria-label="Go to slide 1"></button>
                            <button class="showcase-carousel-dot" data-index="1" aria-label="Go to slide 2"></button>
                            <button class="showcase-carousel-dot" data-index="2" aria-label="Go to slide 3"></button>
                            <button class="showcase-carousel-dot" data-index="3" aria-label="Go to slide 4"></button>
                        </div>
                        <span class="showcase-carousel-counter">1 / 4</span>
                    </div>
                    <div class="showcase-carousel-thumbs" data-theme-container="dark">
                        <button class="showcase-carousel-thumb active" data-index="0" aria-label="Status Tab">
                            <img src="/images/showcase/informationbox-dark-status.png" alt="" loading="lazy" />
                        </button>
                        <button class="showcase-carousel-thumb" data-index="1" aria-label="Account Tab">
                            <img src="/images/showcase/informationbox-dark-account.png" alt="" loading="lazy" />
                        </button>
                        <button class="showcase-carousel-thumb" data-index="2" aria-label="OTP Tab (CodeGlyphX)">
                            <img src="/images/showcase/informationbox-dark-otp.png" alt="" loading="lazy" />
                        </button>
                        <button class="showcase-carousel-thumb" data-index="3" aria-label="Troubleshoot Tab">
                            <img src="/images/showcase/informationbox-dark-troubleshoot.png" alt="" loading="lazy" />
                        </button>
                    </div>
                    <div class="showcase-carousel-thumbs" data-theme-container="light" style="display:none">
                        <button class="showcase-carousel-thumb active" data-index="0" aria-label="Status Tab">
                            <img src="/images/showcase/informationbox-light-status.png" alt="" loading="lazy" />
                        </button>
                        <button class="showcase-carousel-thumb" data-index="1" aria-label="Account Tab">
                            <img src="/images/showcase/informationbox-light-account.png" alt="" loading="lazy" />
                        </button>
                        <button class="showcase-carousel-thumb" data-index="2" aria-label="OTP Tab (CodeGlyphX)">
                            <img src="/images/showcase/informationbox-light-otp.png" alt="" loading="lazy" />
                        </button>
                        <button class="showcase-carousel-thumb" data-index="3" aria-label="Troubleshoot Tab">
                            <img src="/images/showcase/informationbox-light-troubleshoot.png" alt="" loading="lazy" />
                        </button>
                    </div>
                </div>
            </div>
            <script type="application/json" class="carousel-data" data-carousel="carousel-informationbox">
            {"light":["Status Tab","Account Tab","OTP Tab (CodeGlyphX)","Troubleshoot Tab"],"dark":["Status Tab","Account Tab","OTP Tab (CodeGlyphX)","Troubleshoot Tab"]}
            </script>
            <div class="showcase-actions">
                <a href="https://github.com/EvotecIT/InformationBox" target="_blank" rel="noopener" class="btn btn-secondary"><svg viewBox="0 0 24 24" fill="currentColor" class="btn-icon"><path d="M12 0c-6.626 0-12 5.373-12 12 0 5.302 3.438 9.8 8.207 11.387.599.111.793-.261.793-.577v-2.234c-3.338.726-4.033-1.416-4.033-1.416-.546-1.387-1.333-1.756-1.333-1.756-1.089-.745.083-.729.083-.729 1.205.084 1.839 1.237 1.839 1.237 1.07 1.834 2.807 1.304 3.492.997.107-.775.418-1.305.762-1.604-2.665-.305-5.467-1.334-5.467-5.931 0-1.311.469-2.381 1.236-3.221-.124-.303-.535-1.524.117-3.176 0 0 1.008-.322 3.301 1.23.957-.266 1.983-.399 3.003-.404 1.02.005 2.047.138 3.006.404 2.291-1.552 3.297-1.23 3.297-1.23.653 1.653.242 2.874.118 3.176.77.84 1.235 1.911 1.235 3.221 0 4.609-2.807 5.624-5.479 5.921.43.372.823 1.102.823 2.222v3.293c0 .319.192.694.801.576 4.765-1.589 8.199-6.086 8.199-11.386 0-6.627-5.373-12-12-12z"/></svg>View on GitHub</a>
                <a href="https://github.com/EvotecIT/InformationBox/releases" target="_blank" rel="noopener" class="btn btn-outline"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" class="btn-icon"><path d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4"/></svg>Download</a>
                <span class="showcase-status"><span class="status-dot" style="background: var(--success);"></span>Released</span>
            </div>
        </div>
        <!-- AuthIMO Card -->
        <div class="showcase-card showcase-card-large">
            <div class="showcase-header">
                <div class="showcase-icon"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5"><path d="M12 15v2m-6 4h12a2 2 0 002-2v-6a2 2 0 00-2-2H6a2 2 0 00-2 2v6a2 2 0 002 2zm10-10V7a4 4 0 00-8 0v4h8z"/></svg></div>
                <div class="showcase-title">
                    <h2>AuthIMO</h2>
                    <span class="showcase-badge">Windows Desktop</span>
                </div>
            </div>
            <div class="showcase-meta">
                <span class="showcase-tech">.NET 8</span>
                <span class="showcase-tech">WPF</span>
                <span class="showcase-tech">MVVM</span>
            </div>
            <p class="showcase-description">A Windows-first authenticator app + cross-platform .NET library for MFA codes, designed for enterprise and power users. Uses CodeGlyphX for QR code scanning via screen capture to import accounts from any authenticator app. Features a guided setup wizard, PIN protection for quick access, and optional tenant locking for enterprise deployments.</p>
            <div class="showcase-details">
                <h4>Key Features</h4>
                <ul>
                    <li><strong>QR Code Scanning:</strong> Screen capture-based scanning using CodeGlyphX - no camera needed, works with any QR displayed on screen</li>
                    <li><strong>TOTP &amp; HOTP Support:</strong> Full RFC 6238/4226 compliance with configurable periods, digits, and algorithms (SHA1/SHA256/SHA512)</li>
                    <li><strong>PIN Quick Unlock:</strong> Set a numeric PIN for fast vault access without entering your full master password every time</li>
                    <li><strong>Setup Wizard:</strong> Guided first-run experience walks users through vault creation, security options, and initial account setup</li>
                    <li><strong>Tenant Locking:</strong> Enterprise feature to restrict the app to specific Azure AD/Entra ID tenants, preventing unauthorized use</li>
                    <li><strong>AES-256-GCM Encryption:</strong> Industry-standard vault encryption with PBKDF2 key derivation, optional DPAPI or TPM hardware protection</li>
                    <li><strong>Flexible Import:</strong> Google Authenticator migration QR, PSKC (RFC 6030), Entra OATH CSV, and manual otpauth:// URI entry</li>
                    <li><strong>Dual Interface:</strong> Compact minimal mode for quick code access, full vault management for organizing accounts</li>
                </ul>
            </div>
            <div class="showcase-features">
                <span class="showcase-feature"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" width="16" height="16"><path d="M5 13l4 4L19 7"/></svg>QR Screen Scanning</span>
                <span class="showcase-feature"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" width="16" height="16"><path d="M5 13l4 4L19 7"/></svg>PIN Quick Unlock</span>
                <span class="showcase-feature"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" width="16" height="16"><path d="M5 13l4 4L19 7"/></svg>Setup Wizard</span>
                <span class="showcase-feature"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" width="16" height="16"><path d="M5 13l4 4L19 7"/></svg>Tenant Locking</span>
                <span class="showcase-feature"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" width="16" height="16"><path d="M5 13l4 4L19 7"/></svg>AES-256 Encryption</span>
                <span class="showcase-feature"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" width="16" height="16"><path d="M5 13l4 4L19 7"/></svg>TPM/DPAPI Support</span>
                <span class="showcase-feature"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" width="16" height="16"><path d="M5 13l4 4L19 7"/></svg>TOTP & HOTP</span>
                <span class="showcase-feature"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" width="16" height="16"><path d="M5 13l4 4L19 7"/></svg>Light & Dark Themes</span>
            </div>
            <div class="showcase-gallery" data-carousel="carousel-authimo">
                <div class="showcase-gallery-tabs">
                    <button class="showcase-gallery-tab active" data-theme="dark">Dark Theme</button>
                    <button class="showcase-gallery-tab" data-theme="light">Light Theme</button>
                </div>
                <div class="showcase-carousel">
                    <div class="showcase-carousel-viewport">
                        <div class="showcase-carousel-slide active" data-theme="dark" data-index="0">
                            <img src="/images/showcase/authimo-dark-minimal.png" alt="AuthIMO dark theme - Minimal view showing OTP code with countdown timer" loading="lazy" />
                        </div>
                        <div class="showcase-carousel-slide" data-theme="dark" data-index="1">
                            <img src="/images/showcase/authimo-dark-scanqr.png" alt="AuthIMO dark theme - QR code scanning using CodeGlyphX screen capture" loading="lazy" />
                        </div>
                        <div class="showcase-carousel-slide" data-theme="dark" data-index="2">
                            <img src="/images/showcase/authimo-dark-wizard1.png" alt="AuthIMO dark theme - Setup wizard welcome screen" loading="lazy" />
                        </div>
                        <div class="showcase-carousel-slide" data-theme="dark" data-index="3">
                            <img src="/images/showcase/authimo-dark-wizard2.png" alt="AuthIMO dark theme - Account management with multiple OTP entries" loading="lazy" />
                        </div>
                        <div class="showcase-carousel-slide" data-theme="light" data-index="0" style="display:none">
                            <img src="/images/showcase/authimo-light-minimal.png" alt="AuthIMO light theme - Minimal view showing OTP code with countdown timer" loading="lazy" />
                        </div>
                        <div class="showcase-carousel-slide" data-theme="light" data-index="1" style="display:none">
                            <img src="/images/showcase/authimo-light-scanqr.png" alt="AuthIMO light theme - QR code scanning using CodeGlyphX screen capture" loading="lazy" />
                        </div>
                        <div class="showcase-carousel-slide" data-theme="light" data-index="2" style="display:none">
                            <img src="/images/showcase/authimo-light-wizard1.png" alt="AuthIMO light theme - Setup wizard welcome screen" loading="lazy" />
                        </div>
                        <div class="showcase-carousel-slide" data-theme="light" data-index="3" style="display:none">
                            <img src="/images/showcase/authimo-light-wizard2.png" alt="AuthIMO light theme - Account management with multiple OTP entries" loading="lazy" />
                        </div>
                        <button class="showcase-carousel-nav prev" aria-label="Previous image"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M15 19l-7-7 7-7"/></svg></button>
                        <button class="showcase-carousel-nav next" aria-label="Next image"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M9 5l7 7-7 7"/></svg></button>
                    </div>
                    <div class="showcase-carousel-footer">
                        <span class="showcase-carousel-caption">Minimal Mode</span>
                        <div class="showcase-carousel-dots">
                            <button class="showcase-carousel-dot active" data-index="0" aria-label="Go to slide 1"></button>
                            <button class="showcase-carousel-dot" data-index="1" aria-label="Go to slide 2"></button>
                            <button class="showcase-carousel-dot" data-index="2" aria-label="Go to slide 3"></button>
                            <button class="showcase-carousel-dot" data-index="3" aria-label="Go to slide 4"></button>
                        </div>
                        <span class="showcase-carousel-counter">1 / 4</span>
                    </div>
                    <div class="showcase-carousel-thumbs" data-theme-container="dark">
                        <button class="showcase-carousel-thumb active" data-index="0" aria-label="Minimal Mode">
                            <img src="/images/showcase/authimo-dark-minimal.png" alt="" loading="lazy" />
                        </button>
                        <button class="showcase-carousel-thumb" data-index="1" aria-label="QR Scanning (CodeGlyphX)">
                            <img src="/images/showcase/authimo-dark-scanqr.png" alt="" loading="lazy" />
                        </button>
                        <button class="showcase-carousel-thumb" data-index="2" aria-label="Setup Wizard">
                            <img src="/images/showcase/authimo-dark-wizard1.png" alt="" loading="lazy" />
                        </button>
                        <button class="showcase-carousel-thumb" data-index="3" aria-label="Account Management">
                            <img src="/images/showcase/authimo-dark-wizard2.png" alt="" loading="lazy" />
                        </button>
                    </div>
                    <div class="showcase-carousel-thumbs" data-theme-container="light" style="display:none">
                        <button class="showcase-carousel-thumb active" data-index="0" aria-label="Minimal Mode">
                            <img src="/images/showcase/authimo-light-minimal.png" alt="" loading="lazy" />
                        </button>
                        <button class="showcase-carousel-thumb" data-index="1" aria-label="QR Scanning (CodeGlyphX)">
                            <img src="/images/showcase/authimo-light-scanqr.png" alt="" loading="lazy" />
                        </button>
                        <button class="showcase-carousel-thumb" data-index="2" aria-label="Setup Wizard">
                            <img src="/images/showcase/authimo-light-wizard1.png" alt="" loading="lazy" />
                        </button>
                        <button class="showcase-carousel-thumb" data-index="3" aria-label="Account Management">
                            <img src="/images/showcase/authimo-light-wizard2.png" alt="" loading="lazy" />
                        </button>
                    </div>
                </div>
            </div>
            <script type="application/json" class="carousel-data" data-carousel="carousel-authimo">
            {"light":["Minimal Mode","QR Scanning (CodeGlyphX)","Setup Wizard","Account Management"],"dark":["Minimal Mode","QR Scanning (CodeGlyphX)","Setup Wizard","Account Management"]}
            </script>
            <div class="showcase-actions">
                <span class="showcase-status"><span class="status-dot"></span>In Development</span>
            </div>
        </div>
    </div>
    <div class="showcase-submit">
        <div class="showcase-submit-content">
            <h3>Building something with CodeGlyphX?</h3>
            <p>We'd love to feature your project here. Share what you've built and inspire others in the community.</p>
            <a href="https://github.com/EvotecIT/CodeGlyphX/issues" target="_blank" rel="noopener" class="btn btn-primary"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" width="18" height="18"><path d="M12 5v14M5 12h14"/></svg>Submit Your Project</a>
        </div>
    </div>
</div>
