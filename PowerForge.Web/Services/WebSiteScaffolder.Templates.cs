namespace PowerForge.Web;

public static partial class WebSiteScaffolder
{
    private const string SimpleLayoutTemplate =
@"<!doctype html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"" />
  <meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
  <title>{{TITLE}}</title>
  {{DESCRIPTION_META}}
  {{CANONICAL}}
  {{PRELOADS}}
  {{CRITICAL_CSS}}
  {{> theme-tokens}}
  {{EXTRA_CSS}}
  {{OPENGRAPH}}
  {{STRUCTURED_DATA}}
  {{HEAD_HTML}}
  {{ASSET_CSS}}
</head>
<body{{BODY_CLASS}}>
  {{> site-header}}
  <main class=""pf-web-content"">
    <div class=""pf-container"">
      <div class=""pf-content"">
        {{CONTENT}}
      </div>
    </div>
  </main>
  {{> site-footer}}
  {{ASSET_JS}}
  {{EXTRA_SCRIPTS}}
</body>
</html>
";

    private const string ScribanLayoutTemplate =
@"<!doctype html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"" />
  <meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
  <title>{{ page.title }}</title>
  {{ description_meta_html }}
  {{ canonical_html }}
  {{ assets.preloads_html }}
  {{ assets.critical_css_html }}
  {{ include ""theme-tokens"" }}
  {{ extra_css_html }}
  {{ assets.css_html }}
  {{ opengraph_html }}
  {{ structured_data_html }}
  {{ head_html }}
</head>
<body{{ if body_class != """" }} class=""{{ body_class }}""{{ end }}>
  {{ include ""site-header"" }}
  <main class=""pf-web-content"">
    <div class=""pf-container"">
      <div class=""pf-content"">
        {{ content }}
      </div>
    </div>
  </main>
  {{ include ""site-footer"" }}
  {{ assets.js_html }}
  {{ extra_scripts_html }}
</body>
</html>
";

    private const string ScribanPostLayoutTemplate =
@"<!doctype html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"" />
  <meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
  <title>{{ page.title }}</title>
  {{ description_meta_html }}
  {{ canonical_html }}
  {{ assets.preloads_html }}
  {{ assets.critical_css_html }}
  {{ include ""theme-tokens"" }}
  {{ extra_css_html }}
  {{ assets.css_html }}
  {{ opengraph_html }}
  {{ structured_data_html }}
  {{ head_html }}
</head>
<body{{ if body_class != """" }} class=""{{ body_class }}""{{ end }}>
  {{ include ""site-header"" }}
  <main class=""pf-web-content"">
    <div class=""pf-container"">
      <article class=""pf-content"">
        <header>
          {{ if page.collection != """" }}<p class=""pf-editorial-meta""><span>{{ page.collection }}</span>{{ if page.date }}<time datetime=""{{ page.date }}"">{{ page.date }}</time>{{ end }}</p>{{ end }}
          <h1>{{ page.title }}</h1>
          {{ if page.description != """" }}<p class=""pf-editorial-summary"">{{ page.description }}</p>{{ end }}
        </header>
        {{ content }}
      </article>
    </div>
  </main>
  {{ include ""site-footer"" }}
  {{ assets.js_html }}
  {{ extra_scripts_html }}
</body>
</html>
";

    private const string ScribanListLayoutTemplate =
@"<!doctype html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"" />
  <meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
  <title>{{ page.title }}</title>
  {{ description_meta_html }}
  {{ canonical_html }}
  {{ assets.preloads_html }}
  {{ assets.critical_css_html }}
  {{ include ""theme-tokens"" }}
  {{ extra_css_html }}
  {{ assets.css_html }}
  {{ opengraph_html }}
  {{ structured_data_html }}
  {{ head_html }}
</head>
<body{{ if body_class != """" }} class=""{{ body_class }}""{{ end }}>
  {{ include ""site-header"" }}
  <main class=""pf-web-content"">
    <div class=""pf-container"">
      <section class=""pf-content"">
        <header>
          {{ if page.collection != """" }}<p class=""pf-editorial-meta""><span>{{ page.collection }}</span></p>{{ end }}
          <h1>{{ page.title }}</h1>
          {{ if page.description != """" }}<p class=""pf-editorial-summary"">{{ page.description }}</p>{{ end }}
        </header>
        {{ content }}
        {{ if items && items.size > 0 }}
          <div class=""pf-editorial-grid"">
            {{ for item in items }}
              <a class=""pf-editorial-card"" href=""{{ item.output_path }}"">
                <p class=""pf-editorial-meta"">
                  <span>{{ item.collection }}</span>
                  {{ if item.date }}<time datetime=""{{ item.date }}"">{{ item.date }}</time>{{ end }}
                </p>
                <h3>{{ item.title }}</h3>
                {{ if item.description != """" }}<p class=""pf-editorial-summary"">{{ item.description }}</p>{{ end }}
                {{ if item.tags && item.tags.size > 0 }}
                  <div class=""pf-editorial-tags"">
                    {{ for tag in item.tags }}
                      <span class=""pf-chip"">{{ tag }}</span>
                    {{ end }}
                  </div>
                {{ end }}
              </a>
            {{ end }}
          </div>

          {{ if pagination && pagination.total_pages > 1 }}
            <nav class=""pf-pagination"" aria-label=""Pagination"">
              <div>
                {{ if pagination.has_previous && pagination.previous_url != """" }}<a href=""{{ pagination.previous_url }}"">Newer posts</a>{{ end }}
              </div>
              <div>
                {{ if pagination.has_next && pagination.next_url != """" }}<a href=""{{ pagination.next_url }}"">Older posts</a>{{ end }}
              </div>
            </nav>
          {{ end }}
        {{ else }}
          <p>No entries yet.</p>
        {{ end }}
      </section>
    </div>
  </main>
  {{ include ""site-footer"" }}
  {{ assets.js_html }}
  {{ extra_scripts_html }}
</body>
</html>
";

    private const string ScribanTaxonomyLayoutTemplate =
@"<!doctype html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"" />
  <meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
  <title>{{ page.title }}</title>
  {{ description_meta_html }}
  {{ canonical_html }}
  {{ assets.preloads_html }}
  {{ assets.critical_css_html }}
  {{ include ""theme-tokens"" }}
  {{ extra_css_html }}
  {{ assets.css_html }}
  {{ opengraph_html }}
  {{ structured_data_html }}
  {{ head_html }}
</head>
<body{{ if body_class != """" }} class=""{{ body_class }}""{{ end }}>
  {{ include ""site-header"" }}
  <main class=""pf-web-content"">
    <div class=""pf-container"">
      <section class=""pf-content"">
        <header>
          <p class=""pf-editorial-meta""><span>Taxonomy</span>{{ if taxonomy && taxonomy.name != """" }}<span>{{ taxonomy.name }}</span>{{ end }}</p>
          <h1>{{ page.title }}</h1>
        </header>
        {{ content }}
        {{ if items && items.size > 0 }}
          <div class=""pf-editorial-grid"">
            {{ for item in items }}
              <a class=""pf-editorial-card"" href=""{{ item.output_path }}"">
                <h3>{{ item.title }}</h3>
                {{ if item.description != """" }}<p class=""pf-editorial-summary"">{{ item.description }}</p>{{ end }}
              </a>
            {{ end }}
          </div>
        {{ else }}
          <p>No taxonomy entries yet.</p>
        {{ end }}
      </section>
    </div>
  </main>
  {{ include ""site-footer"" }}
  {{ assets.js_html }}
  {{ extra_scripts_html }}
</body>
</html>
";

    private const string ScribanTermLayoutTemplate =
@"<!doctype html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"" />
  <meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
  <title>{{ page.title }}</title>
  {{ description_meta_html }}
  {{ canonical_html }}
  {{ assets.preloads_html }}
  {{ assets.critical_css_html }}
  {{ include ""theme-tokens"" }}
  {{ extra_css_html }}
  {{ assets.css_html }}
  {{ opengraph_html }}
  {{ structured_data_html }}
  {{ head_html }}
</head>
<body{{ if body_class != """" }} class=""{{ body_class }}""{{ end }}>
  {{ include ""site-header"" }}
  <main class=""pf-web-content"">
    <div class=""pf-container"">
      <section class=""pf-content"">
        <header>
          <p class=""pf-editorial-meta"">
            {{ if taxonomy && taxonomy.name != """" }}<span>{{ taxonomy.name }}</span>{{ end }}
            {{ if term != """" }}<span>{{ term }}</span>{{ end }}
          </p>
          <h1>{{ page.title }}</h1>
          {{ if taxonomy_term_summary && taxonomy_term_summary.count > 0 }}<p class=""pf-editorial-summary"">{{ taxonomy_term_summary.count }} item(s)</p>{{ end }}
        </header>
        {{ content }}
        {{ if items && items.size > 0 }}
          <div class=""pf-editorial-grid"">
            {{ for item in items }}
              <a class=""pf-editorial-card"" href=""{{ item.output_path }}"">
                <p class=""pf-editorial-meta"">{{ if item.date }}<time datetime=""{{ item.date }}"">{{ item.date }}</time>{{ end }}</p>
                <h3>{{ item.title }}</h3>
                {{ if item.description != """" }}<p class=""pf-editorial-summary"">{{ item.description }}</p>{{ end }}
              </a>
            {{ end }}
          </div>
        {{ else }}
          <p>No entries for this term yet.</p>
        {{ end }}
      </section>
    </div>
  </main>
  {{ include ""site-footer"" }}
  {{ assets.js_html }}
  {{ extra_scripts_html }}
</body>
</html>
";
}
