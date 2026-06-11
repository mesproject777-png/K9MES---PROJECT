import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';

import { SnResultComponent } from './sn-result.component';

const routes: Routes = [
  { path: '', component: SnResultComponent },
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule],
})
export class SnResultRoutingModule {}