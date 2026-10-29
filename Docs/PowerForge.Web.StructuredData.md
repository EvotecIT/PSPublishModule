# Structured data

PowerForge.Web renders JSON-LD from site configuration and page metadata. It uses
the same renderer for `structured_data_html` and the Scriban helper
`pf.structured_data`. Use one of these in a page's head, rather than both.

## Publisher and page identities

Enable `WebPage` to give each canonical page an identity and connect it to its
website and publisher. `Website` and `Organization` then emit on every page,
instead of only the home page. Existing sites keep their previous behavior until
they enable `WebPage`.

```json
"StructuredData": {
  "Enabled": true,
  "WebPage": true,
  "Website": true,
  "Organization": true,
  "OrganizationName": "Example Software",
  "OrganizationLegalName": "Example Software Ltd.",
  "OrganizationUrl": "https://publisher.example/",
  "SoftwareApplication": true,
  "FaqPage": true
}
```

The publisher's name falls back to `OrganizationLegalName`, then the site name.
Its URL falls back to the site base URL. Existing logo, contact and `sameAs`
settings still apply. Set `meta.schema.page_type: ContactPage` on a contact page.
Site-relative organization contact URLs follow published translations, including
translated slugs. If a translation is absent, the existing contact destination
remains available. Explicit absolute contact URLs retain their configured destination. Application markup
declares the language of its page metadata. Breadcrumb labels use page links,
not section anchors or query links; when the brand supplies the home link,
the site name is the fallback home label.
FAQ metadata produces a single `FAQPage` with the canonical page identity.
Contact pages containing FAQs retain both types on that identity, with the
publisher as their subject and the questions in `mainEntity`. Software entities
retain their canonical page link through `mainEntityOfPage` when FAQs are present.

## Articles and authors

Blog and news collections are recognized automatically. An undated guide in a
documentation collection can declare its article type without adding a fictitious
publication date:

```yaml
meta.schema.type: Article
meta.author.name: Example Editorial Team
meta.author.type: Organization
meta.author.url: https://publisher.example/about/
image: /images/event-report.png
```

Supported explicit types are `Article`, `BlogPosting` and `NewsArticle`; the
corresponding site-level `Article` or `NewsArticle` switch still controls output.
Authors default to `Person`. Use `Organization` for an editorial team and give an
author URL that identifies the person or organization. Existing `author_names`
and `author_urls` lists describe multiple authors.

Use a representative image from the article, rather than the site's logo. The
renderer accepts an explicit `article.image` or page image and can use the first
body image on editorial pages. It omits the generic site-wide social image from
article markup when no article image or generated article card is available.
The renderer emits known publication and modification dates; it does not invent a
publication date for undated guides. With `WebPage` enabled, the canonical page
and its article reference one another through stable identifiers.

Google's [Article guidance](https://developers.google.com/search/docs/appearance/structured-data/article)
lists recommended properties rather than required ones. Supply applicable facts
that agree with the visible content. Search appearance still depends on Google's
crawl and indexing decisions.

## Data supplied by a theme

Use `pf.structured_data` when the visible page already gets its facts from
`data/*.json`. The argument is a metadata object with the same nested keys as
front matter; it only affects this invocation and does not change page metadata.
Both site-level disabling and `meta.structured_data: false` still apply.

```scriban
{{ metadata = { software: {
  name: data.product.name,
  application_category: "BusinessApplication",
  operating_system: "Windows",
  id: site.base_url + "/#software",
  url: "/",
  price: data.pricing.minimum_monthly_price,
  price_currency: data.pricing.currency,
  offer_url: site.base_url + "/pricing/",
  eligible_quantity: { value: data.pricing.minimum_seats, unit_text: "seats" },
  reference_quantity: { value: 1, unit_code: "MON", unit_text: "month" }
} } }}
{{ pf.structured_data metadata }}
```

The site supplies the actual minimum price and quantity; the renderer does not
calculate business-specific pricing. `reference_quantity` describes the unit to
which that price applies. For example, `MON` with value `1` means one month.
`eligible_quantity` can use `value` or `min_value` / `max_value`, with `unit_code`
and/or `unit_text`. An optional `offer_id`, `offer_name` or `offer_description`
describes the offer without changing the application's name or URL.

For a grouped FAQ, pass `{ faq: { sections: data.faq.sections } }`. Each section
has an `items` array of question/answer objects. Answers can be strings or arrays
of paragraphs; paragraphs retain their order and are separated by blank lines.
The existing `faq.questions` and `faq.items` metadata remains supported.

## Libraries and source code

Use `SoftwareApplication` for an application. To describe source code or a
library, enable `StructuredData.SoftwareSourceCode` and supply `source_code`
metadata instead:

```yaml
meta.source_code.name: Example.Documents
meta.source_code.code_repository: https://github.com/example/documents
meta.source_code.programming_language: C#
meta.source_code.runtime_platform: .NET
meta.source_code.license: https://opensource.org/license/mit/
```

The source-code profile has no default offer, rating or application claim.
Application and source-code profiles are independently enabled and require their
own metadata. An optional `meta.source_code.id` sets a stable identity even when
page linking is disabled.

## Validation

Build the site, then run the existing `seo-doctor` pipeline task to check JSON-LD
syntax and supported profile fields. Check the rendered page with the
[Schema.org validator](https://validator.schema.org/) and, for a supported Google
feature, the [Rich Results Test](https://search.google.com/test/rich-results).

The doctor accepts JSON-LD `@graph` containers whose entities declare their own
types. The container does not need an additional `@type`; the doctor still
checks the profiles inside the graph and reports a payload with no declared type.

Google's supported search features change independently of Schema.org. FAQ markup
still describes visible questions and answers, but Google stopped showing FAQ
rich results in May 2026. Its sitelinks search box was removed in November 2024;
existing `SearchAction` markup can remain without causing a Search Console error.
See the [Google documentation updates](https://developers.google.com/search/updates)
and [sitelinks search box announcement](https://developers.google.com/search/blog/2024/10/sitelinks-search-box).

Schema.org validity and Google search eligibility are separate. Google's
[software application requirements](https://developers.google.com/search/docs/appearance/structured-data/software-app)
include real review or rating evidence as well as the application name and offer
price. Supply only facts visible to users; do not add invented ratings to satisfy
a search feature. Valid markup does not guarantee a particular search result.
