export const environment = {
  apiBaseUrl: 'http://localhost:5156',
  // null: canonical URLs fall back to document.location (so local testing
  // never mistakes localhost for the production domain).
  canonicalOrigin: null as string | null,
  // Path the edition is served under: '' for the US site, '/uk' for the UK
  // section (see core/site-path.ts).
  basePath: '',
};
