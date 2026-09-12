import { Pipe, PipeTransform } from '@angular/core';

import { formatPrice } from './market';

/** `{{ deal.currentPrice | price }}` → "$39.97" in the site's market currency. */
@Pipe({ name: 'price' })
export class PricePipe implements PipeTransform {
  transform(value: number | null | undefined): string {
    return value == null ? '' : formatPrice(value);
  }
}
