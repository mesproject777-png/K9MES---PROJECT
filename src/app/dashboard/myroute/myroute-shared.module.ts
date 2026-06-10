import { NgModule } from '@angular/core';

import { SharedModule } from '../shared/shared.module';
import { MyrouteComponent } from './myroute.component';

@NgModule({
  declarations: [
    MyrouteComponent,
  ],
  imports: [
    SharedModule,
  ],
  exports: [
    MyrouteComponent,
  ],
})
export class MyrouteSharedModule {}