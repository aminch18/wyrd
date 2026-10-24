import { DOC_SECTIONS } from '../data/docs.ts'
import type { Route } from './navigation.ts'

export const SITE_URL = 'https://aminch18.github.io/wyrd/'
export const HOME_TITLE = 'Wyrd — Deterministic Event-Driven Processes for .NET'
export const HOME_DESCRIPTION =
    'An open-source .NET 10 / C# 14 library for pure functional DDD, declarative sagas, retries, compensation and versioned message contracts.'

export function pageMetadata(route: Route) {
    const section = DOC_SECTIONS.find(({ id }) => id === route.section)
    if (route.page === 'docs' && !section)
        throw new Error(`Unknown documentation section: ${route.section}`)
    return {
        title:
            route.page === 'home'
                ? HOME_TITLE
                : `${section!.title} — Wyrd .NET documentation`,
        description:
            route.page === 'home'
                ? HOME_DESCRIPTION
                : `${section!.subtitle} Learn ${section!.title.toLowerCase()} with Wyrd, the deterministic event-driven process library for .NET.`,
        url:
            route.page === 'home'
                ? SITE_URL
                : `${SITE_URL}docs/${section!.id}/`,
        image: `${SITE_URL}wyrd-logo-dark-compact.png`,
    }
}

function escape(value: string) {
    return value
        .replaceAll('&', '&amp;')
        .replaceAll('"', '&quot;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;')
}

export function structuredData(route: Route) {
    const page = pageMetadata(route)
    const website = {
        '@type': 'WebSite',
        '@id': `${SITE_URL}#website`,
        name: 'Wyrd',
        url: SITE_URL,
        inLanguage: 'en',
    }
    const webpage = {
        '@type': route.page === 'home' ? 'WebPage' : 'TechArticle',
        '@id': page.url,
        url: page.url,
        name: page.title,
        headline: page.title,
        description: page.description,
        inLanguage: 'en',
        isPartOf: { '@id': website['@id'] },
    }
    const extra =
        route.page === 'home'
            ? {
                  '@type': 'SoftwareSourceCode',
                  name: 'Wyrd',
                  description: HOME_DESCRIPTION,
                  codeRepository: 'https://github.com/aminch18/wyrd',
                  programmingLanguage: ['C#'],
                  runtimePlatform: '.NET 10',
                  license: 'https://github.com/aminch18/wyrd/blob/main/LICENSE',
                  url: SITE_URL,
              }
            : {
                  '@type': 'BreadcrumbList',
                  itemListElement: [
                      {
                          '@type': 'ListItem',
                          position: 1,
                          name: 'Wyrd',
                          item: SITE_URL,
                      },
                      {
                          '@type': 'ListItem',
                          position: 2,
                          name: DOC_SECTIONS.find(
                              ({ id }) => id === route.section,
                          )!.title,
                          item: page.url,
                      },
                  ],
              }
    return {
        '@context': 'https://schema.org',
        '@graph': [website, webpage, extra],
    }
}

export function renderMetadata(route: Route) {
    const page = pageMetadata(route)
    const meta = (key: string, value: string, attribute = 'name') =>
        `<meta ${attribute}="${key}" content="${escape(value)}" />`
    return [
        `<title>${escape(page.title)}</title>`,
        meta('description', page.description),
        `<link rel="canonical" href="${page.url}" />`,
        meta('robots', 'index, follow, max-image-preview:large'),
        meta(
            'og:type',
            route.page === 'home' ? 'website' : 'article',
            'property',
        ),
        meta('og:site_name', 'Wyrd', 'property'),
        meta('og:locale', 'en_US', 'property'),
        ...['title', 'description', 'url', 'image'].map((key) =>
            meta(`og:${key}`, page[key as keyof typeof page], 'property'),
        ),
        meta(
            'og:image:alt',
            'Wyrd — deterministic event-driven processes for .NET',
            'property',
        ),
        meta('twitter:card', 'summary'),
        ...['title', 'description', 'image'].map((key) =>
            meta(`twitter:${key}`, page[key as keyof typeof page]),
        ),
        meta('twitter:image:alt', 'Wyrd logo'),
        `<script type="application/ld+json">${JSON.stringify(structuredData(route)).replaceAll('<', '\\u003c')}</script>`,
    ].join('\n')
}
