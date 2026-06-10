import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';

import { GenerateSnComponent } from '../manager/generatesn/generatesn.component';
import { permissionGuard } from '../../guards/permission.guard';
import { StationLoginsComponent } from './station-logins.component';
import { WorkOrderComponent } from './work-order.component';

const routes: Routes = [
  { path: '', component: WorkOrderComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/home' } },
  { path: 'station-logins', component: StationLoginsComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/home' } },
  { path: 'SNList', component: GenerateSnComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/manager/generatesn' } },
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule],
})
export class WorkOrderRoutingModule {}
