import { Component } from '@angular/core';

type EngineeringActionCard = {
  id: string;
  title: string;
  description: string;
  icon: string;
  route: string;
};

@Component({
  selector: 'app-engineeringmenu',
  standalone: false,
  templateUrl: './engineeringmenu.component.html',
  styleUrl: './engineeringmenu.component.scss'
})
export class EngineeringmenuComponent {
  readonly cards: EngineeringActionCard[] = [
    {
      id: 'hlEngineering1',
      title: 'Product Line',
      description: 'Manage product lines used in build.',
      icon: 'category',
      route: '/dashboard/engineering/productline'
    },
    {
      id: 'hlEngineering4',
      title: 'Part Number',
      description: 'Create and manage part numbers.',
      icon: 'tag',
      route: '/dashboard/engineering/partnumber'
    },
    {
      id: 'hlEngineering5',
      title: 'Item Revisions',
      description: 'Maintain item revision history.',
      icon: 'history',
      route: '/dashboard/engineering/itemrevisions'
    },
    {
      id: 'hlEngineering6',
      title: 'Routing',
      description: 'Configure routing steps and flow.',
      icon: 'route',
      route: '/dashboard/engineering/routing'
    },
    {
      id: 'hlEngineering9',
      title: 'Five Step Wizard',
      description: 'Build PartNumber, WorkOrder, Routing and BOM flow.',
      icon: 'linear_scale',
      route: '/dashboard/engineering/five-step-wizard'
    },
    {
      id: 'hlEngineering10',
      title: 'Assembly Definition',
      description: 'Define assembly structure and rules.',
      icon: 'account_tree',
      route: '/dashboard/engineering/assembly-definition'
    },
    {
      id: 'hlEngineering8',
      title: 'EPV Upload',
      description: 'Upload EPV data for processing.',
      icon: 'upload_file',
      route: '/dashboard/engineering/epv-upload'
    }
  ];
}
