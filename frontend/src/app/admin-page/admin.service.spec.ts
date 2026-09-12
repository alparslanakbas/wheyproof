import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { AdminService } from './admin.service';

describe('AdminService visibility endpoints', () => {
  let service: AdminService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AdminService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads brands from the protected same-origin path, not the API subdomain', () => {
    service.brands().subscribe();
    const request = http.expectOne('/admin/api/brands');
    expect(request.request.method).toBe('GET');
    request.flush([]);
  });

  it('encodes the product search and hidden filter in the URL', () => {
    service.products('whey protein', true).subscribe();
    const request = http.expectOne('/admin/api/products?search=whey+protein&hiddenOnly=true');
    expect(request.request.method).toBe('GET');
    request.flush([]);
  });

  it('updates brand and product status with an isActive-only body', () => {
    service.setBrandActive(12, false).subscribe();
    const brandRequest = http.expectOne('/admin/api/brands/12');
    expect(brandRequest.request.method).toBe('PUT');
    expect(brandRequest.request.body).toEqual({ isActive: false });
    brandRequest.flush({ id: 12, name: 'Sample', isActive: false });

    service.setProductActive(34, true).subscribe();
    const productRequest = http.expectOne('/admin/api/products/34');
    expect(productRequest.request.method).toBe('PUT');
    expect(productRequest.request.body).toEqual({ isActive: true });
    productRequest.flush({ id: 34, name: 'Sample product', isActive: true });
  });
});
