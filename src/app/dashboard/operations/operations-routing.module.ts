import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';

import { permissionGuard } from '../../guards/permission.guard';
import { OperationsAssemblyComponent } from './assembly/assembly.component';
import { OperationsmenuComponent } from './operationsmenu/operationsmenu.component';
import { ClosedPackagesComponent } from './packing/closed-packages/closed-packages.component';
import { OpenPackagesComponent } from './packing/open-packages/open-packages.component';
import { PackagingHistoryComponent } from './packing/packaging-history/packaging-history.component';
import { PackingHierarchyComponent } from './packing/packing-hierarchy/packing-hierarchy.component';
import { ShippedPackagesComponent } from './packing/shipped-packages/shipped-packages.component';
import { SnRouteBackComponent } from './sn-route-back/sn-route-back.component';

const routes: Routes = [
  { path: '', component: OperationsmenuComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/operations/assembly' } },
  { path: 'assembly', component: OperationsAssemblyComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/operations/assembly' } },
  { path: 'sn-route-back', component: SnRouteBackComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/operations/assembly' } },
  { path: 'packing/open', component: OpenPackagesComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/packaging' } },
  { path: 'packing/closed', component: ClosedPackagesComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/packaging' } },
  { path: 'packing/shipped', component: ShippedPackagesComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/packaging' } },
  { path: 'packing/hierarchy', component: PackingHierarchyComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/packaging' } },
  { path: 'packing/history', component: PackagingHistoryComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/packaging' } },
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule],
})
export class OperationsRoutingModule {}
