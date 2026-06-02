import { Component } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../../../../services/auth.service';
import { PackingPackageDetailsResponse, PackingPackageSummary, PackingService, PackageSource } from '../../../../services/packing.service';

@Component({
  selector: 'app-closed-packages',
  standalone: false,
  templateUrl: './closed-packages.component.html',
  styleUrl: './closed-packages.component.scss'
})
export class ClosedPackagesComponent {
  isLoading = false;
  errorMessage = '';
  successMessage = '';

  packages: PackingPackageSummary[] = [];
  selectedPackageId: number | null = null;
  selectedSource: PackageSource = 'PACKAGE';
  selectedPackageDetails: PackingPackageDetailsResponse | null = null;

  constructor(
    private packingService: PackingService,
    private authService: AuthService,
    private route: ActivatedRoute,
    private router: Router
  ) {
    this.refresh();
    this.route.queryParamMap.subscribe((params) => {
      const boxNo = String(params.get('mbx') || '').trim();
      if (boxNo) {
        this.loadMultiboxByNo(boxNo);
      }
    });
  }

  refresh(): void {
    this.isLoading = true;
    this.errorMessage = '';

    this.packingService.listClosed().subscribe({
      next: (response) => {
        this.packages = response.data || [];
        this.isLoading = false;
      },
      error: (error) => {
        this.isLoading = false;
        this.errorMessage = error?.error?.message || 'Unable to load closed packages.';
      }
    });
  }

  selectPackage(pack: PackingPackageSummary): void {
    this.selectedPackageId = pack.id;
    this.selectedSource = pack.source || 'PACKAGE';
    if (this.selectedSource === 'MULTIBOX') {
      this.loadMultiboxByNo(pack.package_no);
      return;
    }

    this.loadSelectedDetails();
  }

  clearSelection(): void {
    this.selectedPackageId = null;
    this.selectedSource = 'PACKAGE';
    this.selectedPackageDetails = null;
    this.errorMessage = '';
    this.successMessage = '';
  }

  shipSelected(): void {
    if (!this.selectedPackageId || this.selectedSource !== 'PACKAGE') {
      return;
    }

    this.isLoading = true;
    this.errorMessage = '';
    this.successMessage = '';

    const changedBy = this.getChangedBy();

    this.packingService.shipPackage(this.selectedPackageId, changedBy).subscribe({
      next: (response) => {
        this.isLoading = false;
        this.successMessage = response.message || 'Package shipped.';
        this.clearSelection();
        this.refresh();
      },
      error: (error) => {
        this.isLoading = false;
        this.errorMessage = error?.error?.message || 'Unable to ship package.';
      }
    });
  }

  private loadSelectedDetails(): void {
    if (!this.selectedPackageId) {
      this.selectedPackageDetails = null;
      return;
    }

    this.packingService.getPackageDetails(this.selectedPackageId).subscribe({
      next: (details) => {
        this.selectedPackageDetails = details;
      },
      error: (error) => {
        this.errorMessage = error?.error?.message || 'Unable to load package details.';
      }
    });
  }

  private loadMultiboxByNo(boxNo: string): void {
    this.isLoading = true;
    this.errorMessage = '';
    this.successMessage = '';

    this.packingService.lookupMultibox(boxNo).subscribe({
      next: (details) => {
        this.isLoading = false;
        this.selectedSource = 'MULTIBOX';
        this.selectedPackageId = details.package.id;
        this.selectedPackageDetails = details;

        if (String(details.package.status).toUpperCase() === 'OPEN') {
          this.router.navigate(['/dashboard/operations/packing/open'], {
            queryParams: { mbx: boxNo, t: Date.now() },
          });
        }
      },
      error: (error) => {
        this.isLoading = false;
        this.errorMessage = error?.error?.message || 'Unable to load MultiBox details.';
      }
    });
  }

  private getChangedBy(): string {
    const current = this.authService.getCurrentUser();
    return current?.user_name || current?.login_id || 'WEB-CLIENT';
  }
}
