import { Component } from '@angular/core';
import { PackingPackageDetailsResponse, PackingPackageSummary, PackingService } from '../../../../services/packing.service';

@Component({
  selector: 'app-shipped-packages',
  standalone: false,
  templateUrl: './shipped-packages.component.html',
  styleUrl: './shipped-packages.component.scss'
})
export class ShippedPackagesComponent {
  isLoading = false;
  errorMessage = '';

  packages: PackingPackageSummary[] = [];
  selectedPackageId: number | null = null;
  selectedPackageDetails: PackingPackageDetailsResponse | null = null;

  constructor(private packingService: PackingService) {
    this.refresh();
  }

  refresh(): void {
    this.isLoading = true;
    this.errorMessage = '';

    this.packingService.listShipped().subscribe({
      next: (response) => {
        this.packages = response.data || [];
        this.isLoading = false;
      },
      error: (error) => {
        this.isLoading = false;
        this.errorMessage = error?.error?.message || 'Unable to load shipped packages.';
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
}
