---
title: Getting Started
description: Install HtmlForgeX and build your first dashboard.
date: 2026-01-10
tags: [install, quickstart]
slug: getting-started
order: 1
nav.title: Start here
nav.weight: 20
collection: docs
aliases:
  - /docs/intro
---

# Getting Started

> [!NOTE]
> HtmlForgeX is UI-only. It does not host data or APIs for you.

```powershell title="Install"
Install-Module HtmlForgeX -Scope CurrentUser
```

{{< include path="shared/snippets/support.md" >}}

## Build a site

Create a site spec, then run the build command.

### Default output

By default, the output is written to `_site` unless you specify `--out`.

## Serve locally

Use `powerforge web serve` to preview the site in a browser.
