import { NgModule } from '@angular/core';

import { MyrouteRoutingModule } from './myroute-routing.module';
import { MyrouteSharedModule } from './myroute-shared.module';

@NgModule({
  imports: [
    MyrouteSharedModule,
    MyrouteRoutingModule,
  ],
})
export class MyrouteModule {}