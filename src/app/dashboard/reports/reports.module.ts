import { NgModule } from '@angular/core';

import { SharedModule } from '../shared/shared.module';
import { ReportsRoutingModule } from './reports-routing.module';
import { ReportsComponent } from './reports.component';
import { ActivityQualityDashboardComponent } from './activity-quality-dashboard/activity-quality-dashboard.component';
import { DebugDashboardComponent } from './debug-dashboard/debug-dashboard.component';
import { ReportsMenuComponent } from './reports-menu/reports-menu.component';
import { ScrapSnComponent } from './scrap-sn/scrap-sn.component';
import { SnChartComponent } from './sn-chart/sn-chart.component';
import { TodayDashboardComponent } from './today-dashboard/today-dashboard.component';
import { UndoScrapComponent } from './undo-scrap/undo-scrap.component';
import { WorkOrderTreeComponent } from './work-order-tree/work-order-tree.component';

@NgModule({
  declarations: [
    ReportsComponent,
    ActivityQualityDashboardComponent,
    DebugDashboardComponent,
    ReportsMenuComponent,
    ScrapSnComponent,
    TodayDashboardComponent,
    UndoScrapComponent,
    WorkOrderTreeComponent,
    SnChartComponent,
  ],
  imports: [
    SharedModule,
    ReportsRoutingModule,
  ],
})
export class ReportsModule {}
