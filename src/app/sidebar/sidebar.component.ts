import { Component } from '@angular/core';
import { AuthService } from '../services/auth.service';

interface SidebarItem {
  label: string;
  route: string;
  icon: string;
  pageKey: string;
}

@Component({
  selector: 'app-sidebar',
  standalone: false,
  templateUrl: './sidebar.component.html',
  styleUrl: './sidebar.component.scss'
})
export class SidebarComponent {
  readonly sidebarItems: SidebarItem[] = [
    { label: 'Dashboard', route: '/dashboard/home', icon: 'dashboard', pageKey: 'dashboard/home' },
    { label: 'Work Flow', route: '/dashboard/workflow', icon: 'account_tree', pageKey: 'dashboard/home' },
    { label: 'Master', route: '/dashboard/master', icon: 'settings', pageKey: 'dashboard/master/menu' },
    { label: 'Manager', route: '/dashboard/manager/menu', icon: 'manage_accounts', pageKey: 'dashboard/manager/menu' },
    { label: 'Engineering', route: '/dashboard/engineering/menu', icon: 'engineering', pageKey: 'dashboard/engineering/menu' },
    { label: 'BOM', route: '/dashboard/bom', icon: 'event_note', pageKey: 'dashboard/bom' },
    { label: 'ECN', route: '/dashboard/ecn', icon: 'assignment', pageKey: 'dashboard/ecn' },
    { label: 'Labels', route: '/dashboard/label', icon: 'label', pageKey: 'dashboard/label' },
    { label: 'Operations', route: '/dashboard/operations', icon: 'precision_manufacturing', pageKey: 'dashboard/operations/assembly' },
    { label: 'Projects', route: '/dashboard/home', icon: 'folder', pageKey: 'dashboard/home' },
    { label: 'Groups', route: '/dashboard/profile', icon: 'groups', pageKey: 'dashboard/profile' },
    { label: 'SN Live Tracker', route: '/dashboard/myroute', icon: 'flag', pageKey: 'dashboard/myroute' },
  ];

  constructor(private authService: AuthService) {}

  canAccess(pageKey: string): boolean {
    return this.authService.hasAccess(pageKey);
  }

}
