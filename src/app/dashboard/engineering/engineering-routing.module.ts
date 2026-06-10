import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';

import { MasterproductlineComponent } from '../master/masterproductline/masterproductline.component';
import { permissionGuard } from '../../guards/permission.guard';
import { AssemblydefinitionComponent } from './assemblydefinition/assemblydefinition.component';
import { EngineeringComponent } from './engineering.component';
import { EngineeringmenuComponent } from './engineeringmenu/engineeringmenu.component';
import { EpvuploadComponent } from './epvupload/epvupload.component';
import { ItemrevisionsComponent } from './itemrevisions/itemrevisions.component';
import { PartnumberComponent } from './partnumber/partnumber.component';
import { PntypeComponent } from './pntype/pntype.component';
import { SnTypeComponent } from './sntype/sntype.component';
import { StationsComponent } from './stations/stations.component';

const routes: Routes = [
  {
    path: '',
    component: EngineeringComponent,
    children: [
      { path: '', component: EngineeringmenuComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/engineering/menu' } },
      { path: 'menu', component: EngineeringmenuComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/engineering/menu' } },
      { path: 'productline', component: MasterproductlineComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/engineering/productline' } },
      { path: 'pntype', component: PntypeComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/engineering/pntype' } },
      { path: 'partnumber', component: PartnumberComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/engineering/partnumber' } },
      { path: 'itemrevisions', component: ItemrevisionsComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/engineering/itemrevisions' } },
      { path: 'stations', component: StationsComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/engineering/menu' } },
      { path: 'five-step-wizard', redirectTo: '/dashboard/workflow', pathMatch: 'full' },
      { path: 'epv-upload', component: EpvuploadComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/engineering/sntype' } },
      { path: 'assembly-definition', component: AssemblydefinitionComponent, canActivate: [permissionGuard], data: { pageKey: 'dashboard/engineering/menu' } },
    ],
  },
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule],
})
export class EngineeringRoutingModule {}
