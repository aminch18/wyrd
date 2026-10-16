# Wyrd website

The marketing site and framework documentation are a static React application built with Vite. Hash-based routes keep the generated site compatible with GitHub Pages without server-side rewrites.

## Development

```bash
npm ci
npm run dev
```

Run `npm run build` before opening a pull request. It performs the strict TypeScript check and creates the production bundle in `dist/`.

## Structure

- `src/app` contains navigation and external-link configuration.
- `src/hooks` owns browser state such as the active route and theme.
- `src/components` contains reusable presentation and layout components.
- `src/pages` composes marketing and documentation pages.
- `src/data` keeps framework snippets and display content out of components.
- `src/styles` separates tokens, layout, page, code, and responsive rules.

The light site theme renders code with the VS Code Dark Modern palette. The dark site theme uses Rider Islands Dark.

## Deployment

The `web.yml` workflow validates pull requests that touch the site. Merges to `main` build with the `/wyrd/` base path and deploy the artifact to the `github-pages` environment. GitHub Pages must use **GitHub Actions** as its publishing source in the repository settings.
