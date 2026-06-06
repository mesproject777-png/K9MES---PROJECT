import { Component } from '@angular/core';
import { forkJoin } from 'rxjs';
import { PackingPackageSummary, PackingService } from '../../../../services/packing.service';

@Component({
  selector: 'app-packaging-history',
  standalone: false,
  templateUrl: './packaging-history.component.html',
  styleUrl: './packaging-history.component.scss'
})
export class PackagingHistoryComponent {
  isLoading = false;
  errorMessage = '';
  openContainers: PackingPackageSummary[] = [];
  previousContainers: PackingPackageSummary[] = [];

  constructor(private packingService: PackingService) {
    this.refresh();
  }

  refresh(): void {
    this.isLoading = true;
    this.errorMessage = '';

    forkJoin({
      open: this.packingService.listOpen(),
      closed: this.packingService.listClosed(),
      shipped: this.packingService.listShipped(),
    }).subscribe({
      next: ({ open, closed, shipped }) => {
        this.openContainers = this.sortByDate(open.data || []);
        this.previousContainers = this.sortByDate([
          ...(closed.data || []),
          ...(shipped.data || []),
        ]);
        this.isLoading = false;
      },
      error: (error) => {
        this.isLoading = false;
        this.openContainers = [];
        this.previousContainers = [];
        this.errorMessage = error?.error?.message || 'Unable to load packaging history.';
      }
    });
  }

  trackByPackage(index: number, pack: PackingPackageSummary): string {
    return `${pack.source || 'PACKAGE'}-${pack.package_type}-${pack.id}-${index}`;
  }

  getStatusClass(status: string): string {
    return String(status || '').trim().toLowerCase();
  }

  getClosedDate(pack: PackingPackageSummary): string | null | undefined {
    return pack.shipped_at || pack.closed_at;
  }

  private sortByDate(packages: PackingPackageSummary[]): PackingPackageSummary[] {
    return [...packages].sort((left, right) => {
      const leftDate = new Date(left.created_at || '').getTime() || 0;
      const rightDate = new Date(right.created_at || '').getTime() || 0;
      return rightDate - leftDate;
    });
  }
}
