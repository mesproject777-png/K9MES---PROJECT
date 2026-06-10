import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';

import { RoleComponent } from '../role/role.component';
import { PntypeComponent } from '../engineering/pntype/pntype.component';
import { SnTypeComponent } from '../engineering/sntype/sntype.component';
import { StationsComponent } from '../engineering/stations/stations.component';
import { permissionGuard } from '../../guards/permission.guard';
import { MasterComponent } from './master.component';
import { MastermenuComponent } from './mastermenu/mastermenu.component';
import { MasterproductlineComponent } from './masterproductline/masterproductline.component';
import { MastersitesComponent } from './mastersites/mastersites.component';
import { MasterstationComponent } from './masterstation/masterstation.component';
import { MasterusersComponent } from './masterusers/masterusers.component';

const routes: Routes = [
  {
    path: '',
    component: MasterComponent,
    children: [
      { path: '', component: MastermenuComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/master/menu' } },
      { path: 'menu', component: MastermenuComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/master/menu' } },
      { path: 'masterstation', component: MasterstationComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/master/masterstation' } },
      { path: 'masterusers', component: MasterusersComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/master/masterusers' } },
      { path: 'masterproductline', component: MasterproductlineComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/master/masterproductline' } },
      { path: 'plant', component: MastersitesComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/master/sites' } },
      { path: 'sites', component: MastersitesComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/master/sites' } },
      { path: 'stations', component: StationsComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/master/stations' } },
      { path: 'pntype', component: PntypeComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/master/pntype' } },
      { path: 'sntype', component: SnTypeComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/engineering/sntype' } },
      { path: 'rolemanagement', component: RoleComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/role' } },
    ],
  },
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule],
})
export class MasterRoutingModule {}
