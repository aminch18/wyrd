# Wyrd website

The marketing site and framework documentation are a static React application built with Vite. The build renders the home page and every documentation chapter to HTML at clean URLs such as `/wyrd/docs/quick-start/`. Nested `index.html` files work on GitHub Pages without server rewrites. React hydrates the rendered content for theme, copy and menu controls; content and chapter links also work without JavaScript. Legacy `#/docs/...` bookmarks redirect to the corresponding clean URL in the browser.

## Development

```bash
npm ci
npm run dev
```

Run `npm run build` before opening a pull request. It checks the API reference against the C# source, tests guide links, performs the strict TypeScript check creates the production bundle in `dist/`, prerenders every page and checks the generated SEO output.

Run `npm run format` after editing and `npm run format:check` to check formatting without rewriting files. CI checks formatting as well as the production build.

## Structure

- `src/app` contains navigation and external-link configuration.
- `src/hooks` owns browser state such as the active route and theme.
- `src/components` contains reusable presentation and layout components.
- `src/pages` composes marketing and documentation pages; each documentation chapter has its own component in `pages/docs`.
- `docs/guides` at the repository root owns the philosophy, DDD migration and read-model chapters. The site imports the Markdown source through Vite and renders fenced examples with the shared code component, so GitHub and the site use the same text. Documentation is loaded separately from the landing page.
- `src/data` keeps framework snippets and display content out of components.
- `src/styles/site.module.css` is the CSS Modules entry point, composed from focused token, layout, page, and responsive stylesheets. Components import its scoped class names explicitly.
- Reusable widgets can own a colocated `*.module.css` file, as `CodeBlock` does. Shared theme tokens are CSS custom properties inherited from the application shell.

Use CSS Modules for component classes. Keep global selectors limited to the base document styles and external highlighter markup. Prefer a reusable component over repeating utility strings or selectors, and add theme colors to the shared tokens rather than introducing one-off overrides.

Code blocks use Shiki's TextMate grammars for C#, shell, and YAML. Only those grammars and two themes are loaded, in a separate lazy chunk. The light site theme uses VS Code Dark Modern (Dark+ tokens with Dark Modern editor colors); the dark site theme uses a TextMate adaptation of Rider Islands Dark. This reproduces the palettes and syntax scopes, not the semantic classifications provided by the IDEs' C# language servers.

## API reference maintenance

`src/data/reference/api.json` contains explicit declaration excerpts for all 49 top-level public types in `src/Wyrd` and `src/Wyrd.Testing`, with authored explanations and examples. The runtime and fluent/pipe builder entries also document every explicit public member individually. Compiler-generated record methods are omitted. Source links point to the corresponding repository files; generic constraints remain in that source.

`npm run check:reference` checks each excerpt against its source, inventories every top-level public type, checks every fluent/pipe builder and runtime declaration, and compares the diagnostic IDs, titles and severities to `Diagnostics.cs`. It also requires per-member guidance for the builders/runtime and links to the suppressor and all three code-fix providers. This is a declaration inventory check, not a C# parser or a substitute for compiling snippets. Update the catalog and examples deliberately when the public API changes; the build rejects stale excerpts or uncovered types/members/rules.

`npm test` verifies internal chapter links and every relative link in the three rendered Markdown guides. Code and design links resolve from the guide's repository folder, including fragments. Unsafe protocols still pass through react-markdown's URL safety filter. The reference includes all declarations and examples in the initial HTML. Syntax highlighting runs only when a reader expands an entry.

## Deployment

The `web.yml` workflow validates pull requests that touch the site. Merges to `main` build with the `/wyrd/` base path and deploy the artifact to the `github-pages` environment. GitHub Pages must use **GitHub Actions** as its publishing source in the repository settings.

## SEO and indexing

`src/app/seo.ts` owns the production URL, per-page titles/descriptions, canonical URLs, Open Graph and Twitter metadata, and JSON-LD (`WebSite`, `WebPage`, `SoftwareSourceCode`, `TechArticle` and breadcrumbs). Descriptions derive from the chapter catalog. `scripts/prerender.mjs` generates complete HTML, `sitemap.xml`, `.nojekyll` and a `noindex` error page. The build verifies that all catalog chapters appear in the sitemap, metadata is unique, and internal page/asset links resolve. No dates, ratings or release guarantees are invented for search engines.

After deployment, verify the `https://aminch18.github.io/wyrd/` URL-prefix property in Google Search Console and submit `https://aminch18.github.io/wyrd/sitemap.xml`. Inspect the home page and a chapter to confirm that Google sees their content and canonical URL. Account verification requires the owner's account; no verification tokens are embedded in source.

Search engines only read `robots.txt` at the **origin root**, `https://aminch18.github.io/robots.txt`. The generated `/wyrd/robots.txt` is a ready-to-copy example, not an effective crawl policy for this project site. If needed, publish its directives from the account-level `aminch18.github.io` repository, preserving any rules for other projects. A missing root robots file does not prevent indexing. Submit the sitemap directly in Search Console regardless. GitHub Pages deployment remains owned by `web.yml`; no separate hosting service is required.
