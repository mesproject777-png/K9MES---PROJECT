import { Component } from '@angular/core';

type OperationsActionCard = {
  id: string;
  title: string;
  description: string;
  icon: string;
  route: string;
};

@Component({
  selector: 'app-operationsmenu',
  standalone: false,
  templateUrl: './operationsmenu.component.html',
  styleUrl: './operationsmenu.component.scss'
})
export class OperationsmenuComponent {
  readonly cards: OperationsActionCard[] = [
    {
      id: 'hlOperationsAssembly',
      title: 'Assembly',
      description: 'Assembly operations workspace.',
      icon: 'build',
      route: '/dashboard/operations/assembly'
    },
    {
      id: 'hlOperationsSnRouteBack',
      title: 'SN Route Back',
      description: 'Route back serial number to previous station.',
      icon: 'undo',
      route: '/dashboard/operations/sn-route-back'
    },
    {
      id: 'hlOperationsOpen',
      title: 'Open Packages',
      description: 'Manage open packages.',
      icon: 'widget_small',
      route: '/dashboard/operations/packing/open'
    },
    {
      id: 'hlOperationsClosed',
      title: 'Closed Packages',
      description: 'Review closed packages.',
      icon: 'inventory_2',
      route: '/dashboard/operations/packing/closed'
    },
    {
      id: 'hlOperationsShipped',
      title: 'Shipped Packages',
      description: 'Track shipped packages.',
      icon: 'local_shipping',
      route: '/dashboard/operations/packing/shipped'
    },
    {
      id: 'hlOperationsPackingHierarchy',
      title: 'Packing Hierarchy',
      description: 'View SN to multibox, pallet, and shipment state.',
      icon: 'account_tree',
      route: '/dashboard/operations/packing/hierarchy'
    }
  ];
}
