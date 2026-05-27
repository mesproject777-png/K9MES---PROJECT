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
      id: 'hlPlant',
      title: 'Plant',
      description: 'Manage plant locations and setup.',
      icon: 'factory',
      route: '/dashboard/master/plant'
    },
    {
      id: 'hlSites',
      title: 'Sites',
      description: 'Manage site locations and setup.',
      icon: 'location_on',
      route: '/dashboard/master/sites'
    },
    {
      id: 'hl4',
      title: 'Product Line',
      description: 'Manage product line master data.',
      icon: 'category',
      route: '/dashboard/master/masterproductline'
    },
    {
      id: 'hlStations',
      title: 'Stations',
      description: 'Manage station definitions.',
      icon: 'precision_manufacturing',
      route: '/dashboard/master/stations'
    },
    {
      id: 'hlPnType',
      title: 'PN Type',
      description: 'Configure part-number types.',
      icon: 'sell',
      route: '/dashboard/master/pntype'
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
