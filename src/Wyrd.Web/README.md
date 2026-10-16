# Wyrd website

The marketing site and framework documentation are a static React application built with Vite. Hash-based routes keep the generated site compatible with GitHub Pages without server-side rewrites.

## Development

```bash
npm ci
npm run dev
```

Run `npm run build` before opening a pull request. It performs the strict TypeScript check and creates the production bundle in `dist/`.

Run `npm run format` after editing and `npm run format:check` to check formatting without rewriting files. CI checks formatting as well as the production build.

## Structure

- `src/app` contains navigation and external-link configuration.
- `src/hooks` owns browser state such as the active route and theme.
- `src/components` contains reusable presentation and layout components.
- `src/pages` composes marketing and documentation pages; each documentation chapter has its own component in `pages/docs`.
- `docs/guides` at the repository root owns the philosophy and DDD migration chapters. The site imports the Markdown source through Vite and renders fenced examples with the shared code component, so GitHub and the site use the same text. Documentation is loaded separately from the landing page.
- `src/data` keeps framework snippets and display content out of components.
- `src/styles/site.module.css` is the CSS Modules entry point, composed from focused token, layout, page, and responsive stylesheets. Components import its scoped class names explicitly.
- Reusable widgets can own a colocated `*.module.css` file, as `CodeBlock` does. Shared theme tokens are CSS custom properties inherited from the application shell.

Use CSS Modules for component classes. Keep global selectors limited to the base document styles and external highlighter markup. Prefer a reusable component over repeating utility strings or selectors, and add theme colors to the shared tokens rather than introducing one-off overrides.

Code blocks use Shiki's TextMate grammars for C#, shell, and YAML. Only those grammars and two themes are loaded, in a separate lazy chunk. The light site theme uses VS Code Dark Modern (Dark+ tokens with Dark Modern editor colors); the dark site theme uses a TextMate adaptation of Rider Islands Dark. This reproduces the palettes and syntax scopes, not the semantic classifications provided by the IDEs' C# language servers.

## Deployment

The `web.yml` workflow validates pull requests that touch the site. Merges to `main` build with the `/wyrd/` base path and deploy the artifact to the `github-pages` environment. GitHub Pages must use **GitHub Actions** as its publishing source in the repository settings.
