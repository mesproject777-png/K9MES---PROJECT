import { Component } from '@angular/core';
import { AuthService } from '../../../../services/auth.service';
import { PackingPackageDetailsResponse, PackingPackageSummary, PackingService } from '../../../../services/packing.service';

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
  selectedPackageDetails: PackingPackageDetailsResponse | null = null;

  constructor(
    private packingService: PackingService,
    private authService: AuthService
  ) {
    this.refresh();
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

  selectPackage(packageId: number): void {
    this.selectedPackageId = packageId;
    this.loadSelectedDetails();
  }

  clearSelection(): void {
    this.selectedPackageId = null;
    this.selectedPackageDetails = null;
    this.errorMessage = '';
    this.successMessage = '';
  }

  shipSelected(): void {
    if (!this.selectedPackageId) {
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

  private getChangedBy(): string {
    const current = this.authService.getCurrentUser();
    return current?.user_name || current?.login_id || 'WEB-CLIENT';
  }
}
