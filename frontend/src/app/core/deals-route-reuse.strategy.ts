import { ActivatedRouteSnapshot, DetachedRouteHandle, RouteReuseStrategy } from '@angular/router';

// DealsList serves both '' and 'product/:id' (the product modal is bound to
// the URL). Angular's default strategy destroys and rebuilds the component
// when moving between route configs, which would reset filters, page and
// search. Between routes pointing at the same component, the instance is
// kept and only the route params update.
export class DealsRouteReuseStrategy implements RouteReuseStrategy {
  shouldDetach(): boolean {
    return false;
  }

  store(): void {}

  shouldAttach(): boolean {
    return false;
  }

  retrieve(): DetachedRouteHandle | null {
    return null;
  }

  shouldReuseRoute(future: ActivatedRouteSnapshot, curr: ActivatedRouteSnapshot): boolean {
    if (future.routeConfig === curr.routeConfig) return true;
    return !!future.component && future.component === curr.component;
  }
}
