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
FAQ metadata produces a single `FAQPage` with the canonical page identity.

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
own metadata.

## Validation

Build the site, then run the existing `seo-doctor` pipeline task to check JSON-LD
syntax and supported profile fields. Check the rendered page with the
[Schema.org validator](https://validator.schema.org/) and, for a supported Google
feature, the [Rich Results Test](https://search.google.com/test/rich-results).

Schema.org validity and Google search eligibility are separate. Google's
[software application requirements](https://developers.google.com/search/docs/appearance/structured-data/software-app)
include real review or rating evidence as well as the application name and offer
price. Supply only facts visible to users; do not add invented ratings to satisfy
a search feature. Valid markup does not guarantee a particular search result.
