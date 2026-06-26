# tracespace browser bundles

Vendored browser bundles used by the repository file preview for Gerber and Excellon drill files.

Sources:

- `@tracespace/core` 5.0.0-alpha.0
- `@tracespace/parser` 5.0.0-alpha.0
- `@tracespace/plotter` 5.0.0-alpha.0
- `@tracespace/renderer` 5.0.0-alpha.0
- `@tracespace/identify-layers` 5.0.0-alpha.0
- `@tracespace/xml-id` 4.2.7

The packages are MIT licensed. `xml-id.js` is a small browser global wrapper around `@tracespace/xml-id` so the UMD bundles can run without a build step.
