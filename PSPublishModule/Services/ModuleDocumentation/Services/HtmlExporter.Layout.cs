using HtmlForgeX;

namespace PSPublishModule;

internal sealed partial class HtmlExporter
{
    private static void ConfigureDocumentDefaults(Document document)
    {
        document.Configuration.DataTables.EnableDeferredRendering = true;
        document.Configuration.DataTables.LazyInitByDefault = true;
        document.Configuration.VisNetwork.LazyInitByDefault = true;
        document.Configuration.Tabs.NoHashNavigationByDefault = true;
        ConfigureMarkdownTheme(document);
    }

    private static void ConfigureMarkdownTheme(Document document)
    {
        document.Head.AddCssInline("""
        /* hfx-module-docs-markdown */
        .hfx-md {
            color: var(--tblr-body-color, #1f2937);
            font-size: .96rem;
            line-height: 1.65;
            max-width: 100%;
        }

        .hfx-md > :first-child {
            margin-top: 0 !important;
        }

        .hfx-md > :last-child {
            margin-bottom: 0 !important;
        }

        .hfx-md h1,
        .hfx-md h2,
        .hfx-md h3,
        .hfx-md h4,
        .hfx-md h5,
        .hfx-md h6 {
            color: var(--tblr-emphasis-color, #111827);
            font-weight: 650;
            letter-spacing: 0;
            line-height: 1.25;
            margin: 1.35rem 0 .65rem;
        }

        .hfx-md h1 {
            font-size: 1.55rem;
        }

        .hfx-md h2 {
            border-bottom: 1px solid var(--tblr-border-color, #dbe2ea);
            font-size: 1.35rem;
            padding-bottom: .45rem;
        }

        .hfx-md h3 {
            border-left: 3px solid rgba(var(--tblr-primary-rgb, 32, 107, 196), .55);
            font-size: 1.16rem;
            padding-left: .65rem;
        }

        .hfx-md h4 {
            font-size: 1.05rem;
        }

        .hfx-md h5,
        .hfx-md h6 {
            font-size: 1rem;
        }

        .hfx-md p,
        .hfx-md ul,
        .hfx-md ol,
        .hfx-md blockquote,
        .hfx-md table,
        .hfx-md pre {
            margin-bottom: .9rem;
        }

        .hfx-md ul,
        .hfx-md ol {
            padding-left: 1.35rem;
        }

        .hfx-md li {
            margin: .25rem 0;
        }

        .hfx-md a {
            text-decoration-thickness: .08em;
            text-underline-offset: .18em;
        }

        .hfx-md :not(pre) > code {
            background: rgba(var(--tblr-primary-rgb, 32, 107, 196), .07);
            border: 1px solid rgba(var(--tblr-primary-rgb, 32, 107, 196), .16);
            border-radius: .35rem;
            color: var(--tblr-primary, #206bc4);
            font-size: .9em;
            padding: .08rem .28rem;
            overflow-wrap: anywhere;
        }

        .hfx-md .prism-code-block {
            margin: .85rem 0 1.05rem;
        }

        .hfx-md .prism-code-block .code-toolbar {
            margin: 0;
        }

        .hfx-md pre,
        .hfx-md .prism-code-block pre[class*="language-"] {
            background: linear-gradient(180deg, rgba(248, 250, 252, .98), rgba(241, 245, 249, .94)) !important;
            border: 1px solid rgba(15, 23, 42, .08);
            border-radius: .65rem;
            box-shadow: inset 0 1px 0 rgba(255, 255, 255, .65);
            color: var(--tblr-body-color, #1f2937) !important;
            font-size: .88rem !important;
            line-height: 1.48 !important;
            overflow-x: auto;
            padding: .9rem 1rem !important;
        }

        .hfx-md blockquote {
            background: rgba(var(--tblr-info-rgb, 66, 153, 225), .07);
            border-left: 4px solid rgba(var(--tblr-info-rgb, 66, 153, 225), .55);
            border-radius: .5rem;
            color: var(--tblr-body-color, #1f2937);
            padding: .8rem 1rem;
        }

        .hfx-md table:not(.dataTable) {
            border: 1px solid var(--tblr-border-color, #dbe2ea);
            border-collapse: separate;
            border-radius: .55rem;
            border-spacing: 0;
            overflow: hidden;
            width: 100%;
        }

        .hfx-md table:not(.dataTable) th,
        .hfx-md table:not(.dataTable) td {
            border-bottom: 1px solid var(--tblr-border-color, #dbe2ea);
            padding: .55rem .7rem;
            vertical-align: top;
        }

        .hfx-md table:not(.dataTable) th {
            background: var(--tblr-tertiary-bg, #f6f8fb);
            color: var(--tblr-secondary-color, #4b5563);
            font-weight: 650;
        }

        .hfx-md table:not(.dataTable) tr:last-child > th,
        .hfx-md table:not(.dataTable) tr:last-child > td {
            border-bottom: 0;
        }

        [data-bs-theme="dark"] .hfx-md {
            color: var(--tblr-body-color, #d1d5db);
        }

        [data-bs-theme="dark"] .hfx-md pre,
        [data-bs-theme="dark"] .hfx-md .prism-code-block pre[class*="language-"] {
            background: linear-gradient(180deg, rgba(17, 24, 39, .98), rgba(15, 23, 42, .96)) !important;
            border-color: rgba(148, 163, 184, .18);
            box-shadow: inset 0 1px 0 rgba(255, 255, 255, .05);
            color: var(--tblr-body-color, #d1d5db) !important;
        }

        [data-bs-theme="dark"] .hfx-md blockquote {
            background: rgba(var(--tblr-info-rgb, 66, 153, 225), .12);
            border-left-color: rgba(var(--tblr-info-rgb, 66, 153, 225), .7);
        }
        """);
    }

    private static void ConfigureNestedTabs(TablerTabs tabs, string navWidth = "18rem", string navMaxHeight = "70vh")
    {
        tabs.Settings(settings => settings
            .OrientationResponsive(TabsOrientation.VerticalLeft, TablerBreakpoint.Medium)
            .NavScrollable()
            .NavWidth(navWidth)
            .NavMaxHeight(navMaxHeight)
            .NoHashNavigation()
            .HideNavigationWhenSingleTab());
    }
}
