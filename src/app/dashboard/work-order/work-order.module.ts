import { NgModule } from '@angular/core';

import { SharedModule } from '../shared/shared.module';
import { SnToolsSharedModule } from '../shared/sn-tools-shared.module';
import { StationLoginsComponent } from './station-logins.component';
import { WorkOrderRoutingModule } from './work-order-routing.module';
import { WorkOrderComponent } from './work-order.component';

@NgModule({
  declarations: [
    WorkOrderComponent,
    StationLoginsComponent,
  ],
  imports: [
    SharedModule,
    SnToolsSharedModule,
    WorkOrderRoutingModule,
  ],
})
export class WorkOrderModule {}
