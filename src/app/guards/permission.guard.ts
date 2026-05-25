import { inject } from '@angular/core';
import { ActivatedRouteSnapshot, CanActivateFn, Router, RouterStateSnapshot } from '@angular/router';
import { AuthService } from '../services/auth.service';

const resolvePageKey = (route: ActivatedRouteSnapshot, state: RouterStateSnapshot): string => {
  return route.data['pageKey'] || state.url.replace(/^\//, '');
};

export const permissionGuard: CanActivateFn = (route, state) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (!authService.isAuthenticated()) {
    return router.createUrlTree(['/login']);
  }

  const pageKey = resolvePageKey(route, state);
  if (authService.hasAccess(pageKey)) {
    return true;
  }

  return router.createUrlTree([authService.getFirstAllowedRoute()]);
};
