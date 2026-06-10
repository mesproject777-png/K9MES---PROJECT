import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';

import { permissionGuard } from '../../guards/permission.guard';
import { MyrouteComponent } from '../myroute/myroute.component';
import { GenerateSnComponent } from './generatesn/generatesn.component';
import { ManagerComponent } from './manager.component';
import { ManagermenuComponent } from './managermenu/managermenu.component';
import { PnwochangeComponent } from './pnwochange/pnwochange.component';
import { SgdpoComponent } from './sgdpo/sgdpo.component';
import { WorkordersComponent } from './workorders/workorders.component';

const routes: Routes = [
  {
    path: '',
    component: ManagerComponent,
    children: [
      { path: '', component: ManagermenuComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/manager/menu' } },
      { path: 'menu', component: ManagermenuComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/manager/menu' } },
      { path: 'workorders', component: WorkordersComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/manager/workorders' } },
      { path: 'sgd-po', component: SgdpoComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/manager/sgdpo' } },
      { path: 'generatesn', component: GenerateSnComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/manager/generatesn' } },
      { path: 'pn-wo-change', component: PnwochangeComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/manager/pnwochange' } },
      { path: 'sntracker', component: MyrouteComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/manager/menu' } },
    ],
  },
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule],
})
export class ManagerRoutingModule {}
