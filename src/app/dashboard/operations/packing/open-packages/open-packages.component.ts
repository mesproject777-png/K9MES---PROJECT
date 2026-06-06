import { Component } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../../../../services/auth.service';
import { PackingPackageDetailsResponse, PackingPackageSummary, PackingService, PackageSource, PackageType } from '../../../../services/packing.service';

@Component({
  selector: 'app-open-packages',
  standalone: false,
  templateUrl: './open-packages.component.html',
  styleUrl: './open-packages.component.scss'
})
export class OpenPackagesComponent {
  isLoading = false;
  errorMessage = '';
  successMessage = '';

  packages: PackingPackageSummary[] = [];
  selectedPackageId: number | null = null;
  selectedSource: PackageSource = 'PACKAGE';
  selectedPackageDetails: PackingPackageDetailsResponse | null = null;

  scanQuery = '';

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

    this.packingService.listOpen().subscribe({
      next: (response) => {
        this.packages = response.data || [];
        this.isLoading = false;
      },
      error: (error) => {
        this.isLoading = false;
        this.errorMessage = error?.error?.message || 'Unable to load open packages.';
      }
    });
  }

  create(type: PackageType): void {
    this.isLoading = true;
    this.errorMessage = '';
    this.successMessage = '';

    const changedBy = this.getChangedBy();

    this.packingService.createPackage(type, changedBy).subscribe({
      next: (response) => {
        this.isLoading = false;
        this.successMessage = `${type} created: ${response.data.package_no}`;
        this.refresh();
      },
      error: (error) => {
        this.isLoading = false;
        this.errorMessage = error?.error?.message || 'Unable to create package.';
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

    if (this.selectedSource !== 'PACKAGE') {
      this.selectedPackageDetails = {
        package: {
          id: pack.id,
          package_no: pack.package_no,
          package_type: pack.package_type,
          status: pack.status,
          source: pack.source,
          created_by: pack.created_by,
          created_at: pack.created_at,
          updated_at: pack.created_at,
          closed_by: pack.closed_by,
          closed_at: pack.closed_at,
          shipped_by: pack.shipped_by,
          shipped_at: pack.shipped_at,
        },
        items: [],
      };
      return;
    }

    this.loadSelectedDetails();
  }

  clearSelection(): void {
    this.selectedPackageId = null;
    this.selectedSource = 'PACKAGE';
    this.selectedPackageDetails = null;
    this.scanQuery = '';
    this.errorMessage = '';
    this.successMessage = '';
  }

  onScanSubmit(): void {
    const scanned = String(this.scanQuery || '').trim();
    this.scanQuery = '';

    this.errorMessage = '';
    this.successMessage = '';

    if (!this.selectedPackageId || this.selectedSource !== 'PACKAGE') {
      this.errorMessage = 'Please select an Open Package first.';
      return;
    }

    if (!scanned) {
      this.errorMessage = 'Please scan SN.';
      return;
    }

    this.isLoading = true;

    const changedBy = this.getChangedBy();

    this.packingService.addToPackage(this.selectedPackageId, scanned, changedBy).subscribe({
      next: (response) => {
        this.isLoading = false;
        this.successMessage = response.message || 'Packed successfully.';
        this.loadSelectedDetails();
        this.refresh();
      },
      error: (error) => {
        this.isLoading = false;
        this.errorMessage = error?.error?.message || 'Unable to pack SN.';
        this.loadSelectedDetails();
      }
    });
  }

  closeSelected(): void {
    if (!this.selectedPackageId || this.selectedSource !== 'PACKAGE') {
      return;
    }

    this.isLoading = true;
    this.errorMessage = '';
    this.successMessage = '';

    const changedBy = this.getChangedBy();

    this.packingService.closePackage(this.selectedPackageId, changedBy).subscribe({
      next: (response) => {
        this.isLoading = false;
        this.successMessage = response.message || 'Package closed.';
        this.clearSelection();
        this.refresh();
      },
      error: (error) => {
        this.isLoading = false;
        this.errorMessage = error?.error?.message || 'Unable to close package.';
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

        if (String(details.package.status).toUpperCase() === 'CLOSED') {
          this.router.navigate(['/dashboard/operations/packing/closed'], {
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
