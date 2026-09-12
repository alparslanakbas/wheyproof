import { Component, OnInit, inject } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';

import { FavoritesService } from '../core/favorites.service';

// Bottom tab bar for narrow screens (below sm): keeps the most visited
// destinations one tap away on every page. Hidden on desktop (`sm:hidden`),
// where the header nav is enough.
@Component({
  selector: 'app-mobile-tab-bar',
  imports: [RouterLink, RouterLinkActive],
  templateUrl: './mobile-tab-bar.html',
})
export class MobileTabBar implements OnInit {
  private readonly favoritesService = inject(FavoritesService);

  // The service's shared signal: watchlist changes on any page update the badge.
  protected readonly favoritesCount = this.favoritesService.count;

  ngOnInit(): void {
    this.favoritesService.ensureCount();
  }
}
