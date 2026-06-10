import { NgModule } from '@angular/core';

import { MasterproductlineComponent } from '../master/masterproductline/masterproductline.component';
import { PntypeComponent } from '../engineering/pntype/pntype.component';
import { SnTypeComponent } from '../engineering/sntype/sntype.component';
import { StationsComponent } from '../engineering/stations/stations.component';
import { SortPipe } from '../../pipes/sort.pipe';
import { SharedModule } from './shared.module';

@NgModule({
  declarations: [
    MasterproductlineComponent,
    PntypeComponent,
    SnTypeComponent,
    StationsComponent,
    SortPipe,
  ],
  imports: [
    SharedModule,
  ],
  exports: [
    MasterproductlineComponent,
    PntypeComponent,
    SnTypeComponent,
    StationsComponent,
  ],
})
export class MasterEngineeringSharedModule {}
