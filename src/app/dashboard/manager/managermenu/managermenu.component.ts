import { Component } from '@angular/core';

type ManagerActionCard = {
  id: string;
  title: string;
  description: string;
  icon: string;
  route: string;
};

@Component({
  selector: 'app-managermenu',
  standalone: false,
  templateUrl: './managermenu.component.html',
  styleUrl: './managermenu.component.scss'
})
export class ManagermenuComponent {
  readonly cards: ManagerActionCard[] = [
    {
	  id: 'hlManager1',
      title: 'List WOs',
      description: 'View, search, and manage work orders.',
      icon: 'fact_check',
      route: '/dashboard/manager/workorders'
    },
    {
	  id: 'hlManagerSgdPo',
      title: 'List SGD PO',
      description: 'Create and manage SGD purchase orders.',
      icon: 'receipt_long',
      route: '/dashboard/manager/sgd-po'
    },
    {
	  id: 'hlManager2',
      title: 'Generate SN',
      description: 'Validate a WO, then generate serial numbers.',
      icon: 'qr_code_2',
      route: '/dashboard/manager/generatesn'
    },
    {
	  id: 'hlManager3',
      title: 'SN Tracker',
      description: 'Track serial status and pass/fail history.',
      icon: 'track_changes',
      route: '/dashboard/manager/sntracker'
    },
    {
	  id: 'hlManager4',
      title: 'Pass/Fail Report',
      description: 'Review pass/fail results and trends.',
      icon: 'analytics',
      route: '/dashboard/reports'
    },
    {
	  id: 'hlManager5',
      title: 'PN & WO Change',
      description: 'Update part number or WO when needed.',
      icon: 'swap_horiz',
      route: '/dashboard/manager/pn-wo-change'
    }
  ];
}


