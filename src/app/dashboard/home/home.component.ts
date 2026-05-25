import { Component } from '@angular/core';

type HomeActionCard = {
  id: string;
  title: string;
  description: string;
  icon: string;
  route: string;
};

@Component({
  selector: 'app-home',
  standalone: false,
  templateUrl: './home.component.html',
  styleUrl: './home.component.scss'
})
export class HomeComponent {
  readonly cards: HomeActionCard[] = [
    {
      id: 'home-manager',
      title: 'Manager',
      description: 'Work orders, SN generation, tracker.',
      icon: 'dashboard',
      route: '/dashboard/manager/menu'
    },
    {
      id: 'home-engineering',
      title: 'Engineering',
      description: 'Part numbers, SN types, routing.',
      icon: 'design_services',
      route: '/dashboard/engineering/menu'
    },
    {
      id: 'home-master',
      title: 'Master',
      description: 'Users, stations, routing masters.',
      icon: 'settings',
      route: '/dashboard/master/menu'
    },
    {
      id: 'home-assembly',
      title: 'Assembly',
      description: 'Assembly operations and tracking.',
      icon: 'build_circle',
      route: '/dashboard/operations/assembly'
    },
    {
      id: 'home-packaging',
      title: 'Packaging',
      description: 'Open, closed, shipped packages.',
      icon: 'inventory_2',
      route: '/dashboard/operations/packing/open'
    },
    {
      id: 'home-reports',
      title: 'Reports',
      description: 'Pass/fail reports and analysis.',
      icon: 'analytics',
      route: '/dashboard/reports'
    }
  ];
}
