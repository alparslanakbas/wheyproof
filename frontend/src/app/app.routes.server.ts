import { RenderMode, ServerRoute } from '@angular/ssr';

export const serverRoutes: ServerRoute[] = [
  {
    // THE ADMIN PANEL IS NOT SERVER-RENDERED.
    //
    // The SSR server does not carry the visitor's session cookie; rendered
    // there, every panel request would return 401 and the panel would open
    // on an "unauthorized" screen every time. It is also a tool screen that
    // search engines should never see, so it needs no server HTML.
    path: 'admin',
    renderMode: RenderMode.Client,
  },
  {
    // Prices change often, so pages render on the server with fresh data on
    // every request instead of being prerendered at build time.
    path: '**',
    renderMode: RenderMode.Server,
  },
];
