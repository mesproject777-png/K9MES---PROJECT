import { NgModule } from '@angular/core';

import { SharedModule } from '../shared/shared.module';
import { OperationsAssemblyComponent } from './assembly/assembly.component';
import { OperationsmenuComponent } from './operationsmenu/operationsmenu.component';
import { ClosedPackagesComponent } from './packing/closed-packages/closed-packages.component';
import { OpenPackagesComponent } from './packing/open-packages/open-packages.component';
import { PackagingHistoryComponent } from './packing/packaging-history/packaging-history.component';
import { PackingHierarchyComponent } from './packing/packing-hierarchy/packing-hierarchy.component';
import { ShippedPackagesComponent } from './packing/shipped-packages/shipped-packages.component';
import { SnRouteBackComponent } from './sn-route-back/sn-route-back.component';

@NgModule({
  declarations: [
    OperationsmenuComponent,
    OperationsAssemblyComponent,
    SnRouteBackComponent,
    OpenPackagesComponent,
    ClosedPackagesComponent,
    ShippedPackagesComponent,
    PackingHierarchyComponent,
    PackagingHistoryComponent,
  ],
  imports: [
    SharedModule,
  ],
  exports: [
    OperationsmenuComponent,
    OperationsAssemblyComponent,
    SnRouteBackComponent,
    OpenPackagesComponent,
    ClosedPackagesComponent,
    ShippedPackagesComponent,
    PackingHierarchyComponent,
    PackagingHistoryComponent,
  ],
})
export class OperationsPagesModule {}
