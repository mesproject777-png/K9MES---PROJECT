import { NgModule } from '@angular/core';

import { GenerateSnComponent } from '../manager/generatesn/generatesn.component';
import { MyrouteSharedModule } from '../myroute/myroute-shared.module';
import { SharedModule } from './shared.module';

@NgModule({
  imports: [
    SharedModule,
    GenerateSnComponent,
    MyrouteSharedModule,
  ],
  exports: [
    GenerateSnComponent,
    MyrouteSharedModule,
  ],
})
export class SnToolsSharedModule {}