import { Component } from '@angular/core';

type MasterActionCard = {
  id: string;
  title: string;
  description: string;
  icon: string;
  route: string;
};

@Component({
  selector: 'app-mastermenu',
  standalone: false,
  templateUrl: './mastermenu.component.html',
  styleUrl: './mastermenu.component.scss'
})
export class MastermenuComponent {
  readonly cards: MasterActionCard[] = [
    {
      id: 'hl1',
      title: 'Users',
      description: 'Manage users and access.',
      icon: 'group',
      route: '/dashboard/master/masterusers'
    },
    {
      id: 'hl3',
      title: 'Routing',
      description: 'Configure routing definitions.',
      icon: 'route',
      route: '/dashboard/master/masterrouting'
    },
    {
      id: 'hl4',
      title: 'Product Line',
      description: 'Manage product line master data.',
      icon: 'category',
      route: '/dashboard/master/masterproductline'
    },
    {
      id: 'hlSites',
      title: 'Sites',
      description: 'Manage site locations and setup.',
      icon: 'location_on',
      route: '/dashboard/master/sites'
    },
    {
      id: 'hl5',
      title: 'Role Management',
      description: 'Define roles and permissions.',
      icon: 'admin_panel_settings',
      route: '/dashboard/master/rolemanagement'
    }
  ];
}
