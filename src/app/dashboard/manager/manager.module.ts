import { NgModule } from '@angular/core';

import { SharedModule } from '../shared/shared.module';
import { SnToolsSharedModule } from '../shared/sn-tools-shared.module';
import { ManagerRoutingModule } from './manager-routing.module';
import { ManagerComponent } from './manager.component';
import { ManagermenuComponent } from './managermenu/managermenu.component';
import { PnwochangeComponent } from './pnwochange/pnwochange.component';
import { SgdpoComponent } from './sgdpo/sgdpo.component';
import { WorkordersComponent } from './workorders/workorders.component';

@NgModule({
  declarations: [
    ManagerComponent,
    ManagermenuComponent,
    WorkordersComponent,
    SgdpoComponent,
  ],
  imports: [
    SharedModule,
    PnwochangeComponent,
    SnToolsSharedModule,
    ManagerRoutingModule,
  ],
})
export class ManagerModule {}
